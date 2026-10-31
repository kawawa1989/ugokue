"""対応点から、頭のメッシュの頂点の移動量を求め、顔の向き（AngleX × AngleY）のキーフォームとして書き出す。"""

from __future__ import annotations

import io
import math
from dataclasses import dataclass, field

import cv2
import numpy as np
from PIL import Image
from scipy import sparse
from scipy.sparse.linalg import spsolve

from ugokue_format.model import Keyform, KeyformGroup, Landmark, Mesh, Model, VertexMatch, View
from ugokue_format.package import ModelPackage

from .correspond import DEPTH_SCALE, Correspondence, correspond, head_frame, rotate_with_depth
from .deform import triangle_quality

# 頂点の移動量の、となりの頂点との差の重み（滑らかさ）と、薄板スプラインの予測に寄せる重み。対応点の重みは信頼度（0〜1）。
SMOOTHNESS = 2.0
PRIOR = 0.05
# 奥行きのわかる、顔以外のパーツ（髪、耳、頭の飾り）では、生成した絵の対応点をこの割合でしか信じず、奥行きから予測した動きに強く寄せる。
DEPTH_FLOW_WEIGHT = 0.2
DEPTH_PRIOR = 1.0
# 顔より手前に描くパーツ（前髪）は、頭の表面からこの距離（頭の半径に対する割合）だけ浮いているとみなす。
SURFACE_OFFSET = 0.05
# 奥行きがわかるとき、顔のパーツの予測は、特徴点からの距離がこの値（頭の半径に対する割合）より近いほど薄板スプラインに、遠いほど楕円体の動きに寄せる。
LANDMARK_REACH = 0.25
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
    rest: np.ndarray,
    triangles: list[list[int]],
    targets: np.ndarray,
    weights: np.ndarray,
    prior: np.ndarray,
    smoothness: float = SMOOTHNESS,
    prior_weight: float | np.ndarray = PRIOR,
    keep_prior_shape: bool = False,
) -> np.ndarray:
    """Σ 重み |移動量 − (対応点 − 頂点)|² + 滑らかさ Σ |となりとの移動量の差|² + 予測の重み Σ |移動量 − 予測|² を最小にする移動量を返す。
    keep_prior_shape が真なら、滑らかさは移動量そのものではなく予測からのずれにかける（予測の形を残す）。"""
    n = len(rest)
    edges = set()
    for i, j, k in triangles:
        for a, b in ((i, j), (j, k), (k, i)):
            edges.add((min(a, b), max(a, b)))
    rows, cols = zip(*edges) if edges else ((), ())
    adjacency = sparse.coo_matrix((np.ones(len(rows)), (rows, cols)), shape=(n, n))
    adjacency = adjacency + adjacency.T
    laplacian = sparse.diags(np.asarray(adjacency.sum(axis=1)).ravel()) - adjacency
    prior_weight = np.broadcast_to(np.asarray(prior_weight, dtype=np.float64), (n,))
    system = (sparse.diags(weights + prior_weight) + smoothness * laplacian).tocsc()
    right = weights[:, None] * (targets - rest) + prior_weight[:, None] * prior
    if keep_prior_shape:
        right = right + smoothness * (laplacian @ prior)
    return np.stack([spsolve(system, right[:, 0]), spsolve(system, right[:, 1])], axis=1)


def solve_without_flips(
    rest: np.ndarray,
    triangles: list[list[int]],
    targets: np.ndarray,
    weights: np.ndarray,
    prior: np.ndarray,
    prior_weight: float | np.ndarray = PRIOR,
    keep_prior_shape: bool = False,
) -> np.ndarray:
    """移動量を解き、三角形が裏返ったら、予測の形もならすようにし、滑らかさを強めて解き直す。最後まで裏返る場合は、最後の解を返す。
    keep_prior_shape は最初の解き方で、奥行きから作った予測のように、形を信じてよい予測のときだけ真にする。"""
    offsets = solve_offsets(rest, triangles, targets, weights, prior, SMOOTHNESS, prior_weight, keep_prior_shape)
    smoothness = SMOOTHNESS
    for _ in range(FLIP_RETRIES + 1):
        if triangle_quality(rest, rest + offsets, triangles)["flipped"] == 0:
            break
        offsets = solve_offsets(rest, triangles, targets, weights, prior, smoothness, prior_weight, keep_prior_shape=False)
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
    part_depths: dict[str, float] | None = None,
    yaw_depth_scale: float = DEPTH_SCALE,
    pivot_depth: float = 0.0,
) -> Estimate:
    """向きを変えた絵ごとに対応点を求め、頭のメッシュの移動量を解いて、AngleX × AngleY の格子のキーフォームグループにする。正面の格子点の移動量は 0。
    part_depths（パーツごとの奥行き。See-through と同じく小さいほど手前）を渡すと、顔以外のパーツは、その奥行きで頭を回したときの動きを予測に使う。
    yaw_depth_scale は左右に回すときの頭の前面の奥行き（r に対する割合。上下は DEPTH_SCALE）、pivot_depth は左右に回す軸の頭の中心からの奥行き（r に対する割合）で、どちらも大きいほど強く回る。"""
    model = package.model
    owner, ids = owner_map(package)
    targets_parts = head_parts(model)
    region = np.isin(owner, [ids.index(p) for p in targets_parts])
    region = cv2.dilate(region.astype(np.uint8), np.ones((2 * region_margin + 1,) * 2, np.uint8)) > 0

    xs = sorted({v.angle_x for v in views} | {0})
    ys = sorted({v.angle_y for v in views} | {0})
    meshes = [m for m in model.meshes if m.part in targets_parts]
    stable = sorted(set(front_landmarks) & (stable_landmarks if stable_landmarks is not None else set(front_landmarks)))
    center, radius = head_frame(np.array([front_landmarks[i] for i in stable], dtype=np.float64))
    others = targets_parts - face_parts(model)
    surface_parts = on_head_surface(part_depths or {}, others)
    depth_map = None
    if part_depths and (found := head_depth(package)) is not None:
        # 奥行きで回すときは、頭の前面を顔の層の形から作る。輪郭は奥行き 0 になり、回しても頭の外形として残る。
        center, depth_map, radius = found
    depth_z = depth_to_z(part_depths or {}, others, radius)
    # 首の付け根のように、頭の中心より奥の軸で左右に回すと、頭全体が向いた側へ動く。上下の回転には効かせない。
    pivot = pivot_depth * radius
    yaw_ratio = yaw_depth_scale / DEPTH_SCALE
    # 予測を薄板スプラインに寄せる範囲も、stable の特徴点（目・鼻・口）からの距離で決める。
    landmark_points = np.array([front_landmarks[i] for i in stable], dtype=np.float64)
    offsets: dict[str, dict[tuple[int, int], np.ndarray]] = {m.id: {(0, 0): np.zeros((len(m.vertices), 2))} for m in meshes}
    estimate = Estimate([])
    for view in views:
        name, exists = view_for(model, view.angle_x, view.angle_y)
        if not exists:
            estimate.views.append(View(name, view.angle_x, view.angle_y, []))
        result = correspond(front_image, view.image, front_landmarks, view.landmarks, view.angle_x, view.angle_y, region, stable_landmarks, pivot)
        estimate.correspondences[(view.angle_x, view.angle_y)] = result
        for landmark_id, (x, y) in sorted(result.normalized_landmarks.items()):
            part = _part_at(owner, ids, front_landmarks[landmark_id])
            if part is not None:
                estimate.landmarks.append(Landmark(landmark_id, part, name, [round(x, 2), round(y, 2)], 1.0, "auto"))
        for mesh in meshes:
            rest = np.array(mesh.vertices, dtype=np.float64)
            target = np.stack([_sample(result.target_x, rest), _sample(result.target_y, rest)], axis=1)
            weight = _sample(result.confidence, rest) * _visible(owner, ids.index(mesh.part), rest)
            if mesh.part in depth_z:
                # 前髪は生え際から毛先まで頭の表面に沿うので、層で一定の奥行きにすると、根元が顔より大きく動いて、髪の下に補完された頭皮がはみ出す。
                if mesh.part in surface_parts and depth_map is not None:
                    surface = _sample(depth_map, rest)
                    z_yaw, z_pitch = yaw_ratio * surface + SURFACE_OFFSET * radius, surface + SURFACE_OFFSET * radius
                else:
                    z_yaw = z_pitch = np.full(len(rest), depth_z[mesh.part])
                prior = turn_head(rest, center, z_yaw, z_pitch, view.angle_x, view.angle_y, pivot) - rest
                solved = solve_without_flips(rest, mesh.triangles, target, weight * DEPTH_FLOW_WEIGHT, prior, DEPTH_PRIOR, keep_prior_shape=True)
            elif depth_z:
                # 薄板スプラインは特徴点から離れると大きく外れ、髪の下に補完された額がはみ出す。遠い頂点は、楕円体の表面として回した動きに寄せる。
                near = landmark_nearness(rest, landmark_points, LANDMARK_REACH * radius)[:, None]
                surface = _sample(depth_map, rest) if depth_map is not None else DEPTH_SCALE * np.sqrt(np.clip(radius**2 - ((rest - center) ** 2).sum(axis=1), 0, None))
                rigid = turn_head(rest, center, yaw_ratio * surface, surface, view.angle_x, view.angle_y, pivot)
                prior = near * result.tps(rest) + (1 - near) * rigid - rest
                if mesh.part == "face":
                    # 顔の層の、特徴点から遠いところ（髪の下に補完された頭皮）は、前髪と同じ楕円体の動きに強く寄せ、前髪の横からはみ出さないようにする。
                    far = (1 - near)[:, 0]
                    solved = solve_without_flips(rest, mesh.triangles, target, weight, prior, PRIOR + (DEPTH_PRIOR - PRIOR) * far, keep_prior_shape=True)
                else:
                    solved = solve_without_flips(rest, mesh.triangles, target, weight, prior)
            else:
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


def turn_head(points: np.ndarray, center: np.ndarray, z_yaw: np.ndarray, z_pitch: np.ndarray, yaw: float, pitch: float, pivot: float) -> np.ndarray:
    """頭を回したときの画面上の位置。横の位置は奥行き z_yaw で、縦の位置は z_pitch で回し、左右には奥行き pivot の軸で回したぶん、頭全体を横に動かす。"""
    x = rotate_with_depth(points, center, z_yaw, yaw, pitch)[:, 0] + pivot * math.sin(math.radians(yaw))
    y = rotate_with_depth(points, center, z_pitch, yaw, pitch)[:, 1]
    return np.stack([x, y], axis=1)


def landmark_nearness(points: np.ndarray, landmarks: np.ndarray, reach: float) -> np.ndarray:
    """各点が、いちばん近い特徴点にどれだけ近いか（0〜1。距離 0 で 1、reach で exp(-1)）。"""
    distance = np.sqrt(((points[:, None, :] - landmarks[None, :, :]) ** 2).sum(axis=2)).min(axis=1)
    return np.exp(-((distance / reach) ** 2))


def face_parts(model: Model) -> set[str]:
    """顔と、親が顔のパーツの ID。顔の特徴点の動き（薄板スプライン）で予測する。"""
    return {p.id for p in model.parts if p.mesh is not None and (p.id == "face" or p.parent == "face")}


def depth_to_z(part_depths: dict[str, float], parts: set[str], radius: float) -> dict[str, float]:
    """パーツの奥行き（小さいほど手前）を、頭の奥行き（手前が正、画素）に換算する。頭のパーツのいちばん手前を楕円体の前面、いちばん奥を背面とみなす。"""
    known = {p: d for p, d in part_depths.items() if p in parts}
    if len(set(known.values())) < 2:
        return {}
    near, far = min(known.values()), max(known.values())
    half = DEPTH_SCALE * radius
    return {p: half - 2 * half * (d - near) / (far - near) for p, d in known.items()}


def on_head_surface(part_depths: dict[str, float], parts: set[str]) -> set[str]:
    """parts のうち、顔の層より手前（奥行きが小さい）のパーツ。顔の奥行きがわからなければ空。"""
    if "face" not in part_depths:
        return set()
    return {p for p in parts if p in part_depths and part_depths[p] < part_depths["face"]}


def head_depth(package: ModelPackage, scale: float = DEPTH_SCALE) -> tuple[np.ndarray, np.ndarray, float] | None:
    """顔の層（髪の下に補完された頭皮を含む）の形をふくらませた、頭の前面の奥行き（手前が正、画素）を、キャンバスの画素ごとに返す。
    輪郭からの距離 d、いちばん内側の距離 r で scale × √(r² − (r − d)²)。円なら球を奥行き方向に縮めた形になり、輪郭では 0。
    頭の中心（顔の層の箱の中心）と r も返す。顔の層がなければ None。"""
    model = package.model
    face = next((p for p in model.parts if p.id == "face" and p.mesh is not None), None)
    if face is None:
        return None
    mesh = next(m for m in model.meshes if m.id == face.mesh)
    mask = _alpha_on_canvas(mesh, package.textures[mesh.texture], (model.canvas.height, model.canvas.width))
    if not mask.any():
        return None
    distance = cv2.distanceTransform(np.pad(mask, 1).astype(np.uint8), cv2.DIST_L2, 5)[1:-1, 1:-1]
    inner = float(distance.max())
    depth = scale * np.sqrt(np.clip(inner**2 - (inner - distance) ** 2, 0, None))
    depth[~mask] = 0
    ys, xs = np.nonzero(mask)
    center = np.array([(xs.min() + xs.max() + 1) / 2, (ys.min() + ys.max() + 1) / 2])
    return center, depth.astype(np.float32), inner


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
