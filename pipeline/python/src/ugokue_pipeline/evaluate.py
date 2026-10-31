"""推定したキーフォームの評価。特徴点のずれ、描いた絵と生成した絵の差、中間の角度での三角形の裏返りと伸びを測る。"""

from __future__ import annotations

from typing import Any

import numpy as np

from ugokue_format.model import KeyformGroup, Mesh, Model

from .deform import deformed_positions, triangle_quality

# 三角形の伸び（特異値）がこの範囲の外なら、極端な伸び・縮みとみなす。
MAX_STRETCH = 3.0
MIN_STRETCH = 1 / 3
MISMATCH_THRESHOLD = 32


def locate(mesh: Mesh, point: tuple[float, float]) -> tuple[int, np.ndarray] | None:
    """基準の形で点を含む三角形の番号と、重心座標を返す。含む三角形がなければ None。"""
    vertices = np.array(mesh.vertices, dtype=np.float64)
    p = np.array(point, dtype=np.float64)
    for index, (i, j, k) in enumerate(mesh.triangles):
        a, b, c = vertices[i], vertices[j], vertices[k]
        matrix = np.array([b - a, c - a]).T
        if abs(np.linalg.det(matrix)) < 1e-9:
            continue
        u, v = np.linalg.solve(matrix, p - a)
        if u >= -1e-9 and v >= -1e-9 and u + v <= 1 + 1e-9:
            return index, np.array([1 - u - v, u, v])
    return None


def move_point(mesh: Mesh, groups: list[KeyformGroup], point: tuple[float, float], values: dict[str, float]) -> np.ndarray | None:
    """基準の形の点を、それを含む三角形の変形に合わせて動かした位置。"""
    found = locate(mesh, point)
    if found is None:
        return None
    index, weights = found
    positions = deformed_positions(mesh, groups, values)
    return weights @ positions[mesh.triangles[index]]


def landmark_error(
    model: Model,
    mesh_of_landmark: dict[str, str],
    front: dict[str, tuple[float, float]],
    target: dict[str, tuple[float, float]],
    values: dict[str, float],
) -> dict[str, float]:
    """正面の特徴点を変形で動かした位置と、向きを変えた絵の特徴点の距離の平均（変形しない場合と、変形した場合）。"""
    meshes = {m.id: m for m in model.meshes}
    groups = model.deformation.keyform_groups
    before, after = [], []
    for landmark_id, mesh_id in mesh_of_landmark.items():
        if landmark_id not in target:
            continue
        moved = move_point(meshes[mesh_id], [g for g in groups if g.mesh == mesh_id], front[landmark_id], values)
        if moved is None:
            continue
        goal = np.array(target[landmark_id])
        before.append(np.linalg.norm(np.array(front[landmark_id]) - goal))
        after.append(np.linalg.norm(moved - goal))
    if not after:
        return {"count": 0}
    return {"count": len(after), "before": round(float(np.mean(before)), 2), "after": round(float(np.mean(after)), 2)}


def image_difference(rendered: np.ndarray, target: np.ndarray, region: np.ndarray) -> dict[str, float]:
    """region の画素で、描いた絵と生成した絵の RGB の差の平均と、差が 32 を超える画素の割合。"""
    diff = np.abs(rendered[..., :3].astype(np.int16) - target[..., :3].astype(np.int16)).mean(axis=-1)[region]
    return {"meanAbsDiff": round(float(diff.mean()), 3), "mismatchRatio": round(float((diff > MISMATCH_THRESHOLD).mean()), 5)}


def mesh_quality(model: Model, meshes: list[str], xs: list[float], ys: list[float]) -> dict[str, Any]:
    """中間の角度を含む各角度で変形したときの、裏返った三角形の数と、伸びの最大・最小。極端な伸びの三角形の数も返す。"""
    by_id = {m.id: m for m in model.meshes}
    flipped, extreme = 0, 0
    largest, smallest = 0.0, float("inf")
    worst: dict[str, int] = {}
    for mesh_id in meshes:
        mesh = by_id[mesh_id]
        groups = [g for g in model.deformation.keyform_groups if g.mesh == mesh_id]
        rest = np.array(mesh.vertices, dtype=np.float64)
        for x in xs:
            for y in ys:
                deformed = deformed_positions(mesh, groups, {"AngleX": x, "AngleY": y})
                quality = triangle_quality(rest, deformed, mesh.triangles)
                flipped += quality["flipped"]
                largest, smallest = max(largest, quality["maxStretch"]), min(smallest, quality["minStretch"])
                bad = quality["flipped"] + int(quality["maxStretch"] > MAX_STRETCH or quality["minStretch"] < MIN_STRETCH)
                if bad:
                    worst[mesh_id] = worst.get(mesh_id, 0) + bad
                    extreme += int(quality["maxStretch"] > MAX_STRETCH or quality["minStretch"] < MIN_STRETCH)
    return {
        "flippedTriangles": flipped,
        "anglesWithExtremeStretch": extreme,
        "maxStretch": round(largest, 3),
        "minStretch": round(smallest, 3),
        "meshesWithProblems": worst,
    }
