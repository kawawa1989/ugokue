"""対応点から、頭のメッシュの頂点の移動量を求め、顔の向き（AngleX × AngleY）のキーフォームとして書き出す。"""

from __future__ import annotations

import io
from dataclasses import dataclass, field

import cv2
import numpy as np
from PIL import Image
from scipy import sparse
from scipy.sparse.linalg import spsolve

from ugokue_format.model import Keyform, KeyformGroup, Landmark, Mesh, Model, VertexMatch, View
from ugokue_format.package import ModelPackage

from .correspond import Correspondence, correspond
from .deform import triangle_quality

# 頂点の移動量の、となりの頂点との差の重み（滑らかさ）と、薄板スプラインの予測に寄せる重み。対応点の重みは信頼度（0〜1）。
SMOOTHNESS = 2.0
PRIOR = 0.05
# 三角形が裏返ったら、滑らかさをこの倍にして解き直す（最大の回数）。
FLIP_RETRY_FACTOR = 4.0
FLIP_RETRIES = 4
# 書き出す頂点の対応の、信頼度の下限。
MIN_MATCH_CONFIDENCE = 0.5
ANGLE_PARAMETERS = ["AngleX", "AngleY"]


@dataclass
class ViewInput:
    """向きを変えた絵1枚。"""

    angle_x: int
    angle_y: int
    image: np.ndarray
    landmarks: dict[str, tuple[float, float]]


@dataclass
class Estimate:
    """推定の結果。キーフォームグループ、対応、向きごとの対応点の推定。"""

    groups: list[KeyformGroup]
    views: list[View] = field(default_factory=list)
    landmarks: list[Landmark] = field(default_factory=list)
    vertex_matches: list[VertexMatch] = field(default_factory=list)
    correspondences: dict[tuple[int, int], Correspondence] = field(default_factory=dict)


def view_for(model: Model, angle_x: int, angle_y: int) -> tuple[str, bool]:
    """その向きのビューの ID と、モデルにすでにあるかを返す。なければ x30_y-20 の形の ID にする。"""
    for view in model.views:
        if view.yaw == angle_x and view.pitch == angle_y:
            return view.id, True
    return f"x{angle_x}_y{angle_y}", False


def head_parts(model: Model) -> set[str]:
    """head の子孫で、メッシュを持つパーツの ID。"""
    parents = {p.id: p.parent for p in model.parts}
    result = set()
    for part in model.parts:
        node = part.parent
        while node is not None and node != "head":
            node = parents.get(node)
        if node == "head" and part.mesh is not None:
            result.add(part.id)
    return result


def owner_map(package: ModelPackage) -> tuple[np.ndarray, list[str]]:
    """基準の形で描いたとき、各画素でいちばん手前に見えるパーツの番号（ない画素は -1）と、パーツ ID の一覧を返す。
    メッシュは頂点の外接矩形にテクスチャを貼ったものとみなす（変換や固定テストモデルの格子のメッシュはそうなっている）。"""
    model = package.model
    meshes = {m.id: m for m in model.meshes}
    canvas = (model.canvas.height, model.canvas.width)
    alphas: dict[str, np.ndarray] = {}
    for part in model.parts:
        if part.mesh is None:
            continue
        mesh = meshes[part.mesh]
        alphas[part.id] = _alpha_on_canvas(mesh, package.textures[mesh.texture], canvas)
    ids = [p.id for p in sorted((p for p in model.parts if p.mesh is not None), key=lambda p: p.draw_order)]
    owner = np.full(canvas, -1, dtype=np.int32)
    for index, part_id in enumerate(ids):
        part = next(p for p in model.parts if p.id == part_id)
        visible = alphas[part_id]
        for clip in part.clip_by:
            visible = visible & alphas.get(clip, np.zeros(canvas, dtype=bool))
        owner[visible] = index
    return owner, ids


def _alpha_on_canvas(mesh: Mesh, png: bytes, canvas: tuple[int, int]) -> np.ndarray:
    vertices = np.array(mesh.vertices)
    x1, y1 = np.floor(vertices.min(axis=0)).astype(int)
    x2, y2 = np.ceil(vertices.max(axis=0)).astype(int)
    alpha = np.array(Image.open(io.BytesIO(png)).convert("RGBA"))[..., 3]
    resized = cv2.resize(alpha, (max(1, x2 - x1), max(1, y2 - y1)), interpolation=cv2.INTER_LINEAR)
    result = np.zeros(canvas, dtype=bool)
    cx1, cy1, cx2, cy2 = max(x1, 0), max(y1, 0), min(x2, canvas[1]), min(y2, canvas[0])
    result[cy1:cy2, cx1:cx2] = resized[cy1 - y1 : cy2 - y1, cx1 - x1 : cx2 - x1] >= 128
    return result


def solve_offsets(
    rest: np.ndarray, triangles: list[list[int]], targets: np.ndarray, weights: np.ndarray, prior: np.ndarray, smoothness: float = SMOOTHNESS
) -> np.ndarray:
    """Σ 重み |移動量 − (対応点 − 頂点)|² + 滑らかさ Σ |となりとの移動量の差|² + 予測の重み Σ |移動量 − 予測|² を最小にする移動量を返す。"""
    n = len(rest)
    edges = set()
    for i, j, k in triangles:
        for a, b in ((i, j), (j, k), (k, i)):
            edges.add((min(a, b), max(a, b)))
    rows, cols = zip(*edges) if edges else ((), ())
    adjacency = sparse.coo_matrix((np.ones(len(rows)), (rows, cols)), shape=(n, n))
    adjacency = adjacency + adjacency.T
    laplacian = sparse.diags(np.asarray(adjacency.sum(axis=1)).ravel()) - adjacency
    system = (sparse.diags(weights + PRIOR) + smoothness * laplacian).tocsc()
    right = weights[:, None] * (targets - rest) + PRIOR * prior
    return np.stack([spsolve(system, right[:, 0]), spsolve(system, right[:, 1])], axis=1)


def solve_without_flips(rest: np.ndarray, triangles: list[list[int]], targets: np.ndarray, weights: np.ndarray, prior: np.ndarray) -> np.ndarray:
    """移動量を解き、三角形が裏返ったら、滑らかさを強めて解き直す。最後まで裏返る場合は、最後の解を返す。"""
    smoothness = SMOOTHNESS
    for _ in range(FLIP_RETRIES + 1):
        offsets = solve_offsets(rest, triangles, targets, weights, prior, smoothness)
        if triangle_quality(rest, rest + offsets, triangles)["flipped"] == 0:
            break
        smoothness *= FLIP_RETRY_FACTOR
    return offsets


def _sample(field: np.ndarray, points: np.ndarray) -> np.ndarray:
    x = np.clip(points[:, 0], 0, field.shape[1] - 1).astype(np.float32)
    y = np.clip(points[:, 1], 0, field.shape[0] - 1).astype(np.float32)
    return cv2.remap(field.astype(np.float32), x[None, :], y[None, :], cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)[0]


def _visible(owner: np.ndarray, index: int, points: np.ndarray) -> np.ndarray:
    """頂点の周り 3×3 画素に、そのパーツが見えている画素があるか。格子の頂点はパーツの縁の外にあることがあるため。"""
    height, width = owner.shape
    result = np.zeros(len(points), dtype=bool)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            x = np.clip(np.round(points[:, 0]).astype(int) + dx, 0, width - 1)
            y = np.clip(np.round(points[:, 1]).astype(int) + dy, 0, height - 1)
            result |= owner[y, x] == index
    return result


def estimate_keyforms(
    package: ModelPackage,
    front_image: np.ndarray,
    front_landmarks: dict[str, tuple[float, float]],
    views: list[ViewInput],
    region_margin: int = 4,
    stable_landmarks: set[str] | None = None,
) -> Estimate:
    """向きを変えた絵ごとに対応点を求め、頭のメッシュの移動量を解いて、AngleX × AngleY の格子のキーフォームグループにする。正面の格子点の移動量は 0。"""
    model = package.model
    owner, ids = owner_map(package)
    targets_parts = head_parts(model)
    region = np.isin(owner, [ids.index(p) for p in targets_parts])
    region = cv2.dilate(region.astype(np.uint8), np.ones((2 * region_margin + 1,) * 2, np.uint8)) > 0

    xs = sorted({v.angle_x for v in views} | {0})
    ys = sorted({v.angle_y for v in views} | {0})
    meshes = [m for m in model.meshes if m.part in targets_parts]
    offsets: dict[str, dict[tuple[int, int], np.ndarray]] = {m.id: {(0, 0): np.zeros((len(m.vertices), 2))} for m in meshes}
    estimate = Estimate([])
    for view in views:
        name, exists = view_for(model, view.angle_x, view.angle_y)
        if not exists:
            estimate.views.append(View(name, view.angle_x, view.angle_y, []))
        result = correspond(front_image, view.image, front_landmarks, view.landmarks, view.angle_x, view.angle_y, region, stable_landmarks)
        estimate.correspondences[(view.angle_x, view.angle_y)] = result
        for landmark_id, (x, y) in sorted(result.normalized_landmarks.items()):
            part = _part_at(owner, ids, front_landmarks[landmark_id])
            if part is not None:
                estimate.landmarks.append(Landmark(landmark_id, part, name, [round(x, 2), round(y, 2)], 1.0, "auto"))
        for mesh in meshes:
            rest = np.array(mesh.vertices, dtype=np.float64)
            target = np.stack([_sample(result.target_x, rest), _sample(result.target_y, rest)], axis=1)
            weight = _sample(result.confidence, rest) * _visible(owner, ids.index(mesh.part), rest)
            prior = result.tps(rest) - rest
            solved = solve_without_flips(rest, mesh.triangles, target, weight, prior)
            offsets[mesh.id][(view.angle_x, view.angle_y)] = solved
            for i in np.nonzero(weight >= MIN_MATCH_CONFIDENCE)[0]:
                estimate.vertex_matches.append(
                    VertexMatch(mesh.id, int(i), name, [round(float(target[i, 0]), 2), round(float(target[i, 1]), 2)], round(float(weight[i]), 3), "auto")
                )

    for mesh in meshes:
        keyforms = []
        for x in xs:
            for y in ys:
                if (x, y) not in offsets[mesh.id]:
                    raise ValueError(f"missing view for AngleX={x}, AngleY={y}")
                keyforms.append(Keyform([x, y], [[round(float(dx), 3), round(float(dy), 3)] for dx, dy in offsets[mesh.id][(x, y)]]))
        estimate.groups.append(KeyformGroup(f"{mesh.id}_angle", mesh.id, list(ANGLE_PARAMETERS), [xs, ys], "linear", keyforms))
    return estimate


def _part_at(owner: np.ndarray, ids: list[str], point: tuple[float, float]) -> str | None:
    x = int(np.clip(round(point[0]), 0, owner.shape[1] - 1))
    y = int(np.clip(round(point[1]), 0, owner.shape[0] - 1))
    return ids[owner[y, x]] if owner[y, x] >= 0 else None


def apply_estimate(package: ModelPackage, estimate: Estimate) -> ModelPackage:
    """推定したキーフォームグループ、向きのビュー、対応を、モデルに加える。同じメッシュの顔の向きのグループがあれば置き換える。"""
    model = package.model
    model.views = model.views + estimate.views
    replaced = {g.mesh for g in estimate.groups}
    kept = [g for g in model.deformation.keyform_groups if not (g.mesh in replaced and g.parameters == ANGLE_PARAMETERS)]
    model.deformation.keyform_groups = kept + estimate.groups
    model.correspondence.landmarks = estimate.landmarks
    model.correspondence.vertex_matches = estimate.vertex_matches
    return package
