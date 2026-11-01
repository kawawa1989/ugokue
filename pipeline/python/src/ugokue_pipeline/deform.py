"""キーフォームによる変形の計算（中間形式の仕様と同じ多重線形補間）と、変形後の三角形の品質の検査、描かれている三角形の判定。"""

from __future__ import annotations

import itertools

import numpy as np

from ugokue_format.model import KeyformGroup, Mesh

# 三角形の頂点と中心のテクスチャの不透明度の平均がこれ以上なら、描かれている三角形とみなす。
OPAQUE_ALPHA = 0.5


def deformed_positions(mesh: Mesh, groups: list[KeyformGroup], values: dict[str, float]) -> np.ndarray:
    """メッシュの頂点に、そのメッシュのキーフォームグループの移動量を補間して足した位置を返す。ないパラメータは 0 とする。"""
    positions = np.array(mesh.vertices, dtype=np.float64)
    for group in groups:
        if group.mesh != mesh.id:
            continue
        offsets = {tuple(k.at): np.asarray(k.offsets, dtype=np.float64) for k in group.keyforms}
        corners = []
        for parameter, keys in zip(group.parameters, group.keys):
            value = min(max(values.get(parameter, 0.0), keys[0]), keys[-1])
            i = next(i for i in range(len(keys) - 1) if keys[i] <= value <= keys[i + 1])
            t = (value - keys[i]) / (keys[i + 1] - keys[i])
            corners.append([(keys[i], 1 - t), (keys[i + 1], t)])
        for combination in itertools.product(*corners):
            weight = float(np.prod([w for _, w in combination]))
            if weight:
                positions += weight * offsets[tuple(k for k, _ in combination)]
    return positions


def triangle_quality(rest: np.ndarray, deformed: np.ndarray, triangles: list[list[int]]) -> dict[str, float]:
    """基準の形から変形後の形への三角形ごとの線形変換を求め、裏返った数と、伸び（特異値）の最大・最小を返す。"""
    flipped = 0
    largest, smallest = 0.0, float("inf")
    for i, j, k in triangles:
        before = np.array([rest[j] - rest[i], rest[k] - rest[i]]).T
        after = np.array([deformed[j] - deformed[i], deformed[k] - deformed[i]]).T
        if abs(np.linalg.det(before)) < 1e-9:
            continue
        transform = after @ np.linalg.inv(before)
        if np.linalg.det(transform) <= 0:
            flipped += 1
        singular = np.linalg.svd(transform, compute_uv=False)
        largest, smallest = max(largest, float(singular[0])), min(smallest, float(singular[-1]))
    return {"flipped": flipped, "maxStretch": largest, "minStretch": smallest if smallest != float("inf") else 1.0}


def opaque_triangles(mesh: Mesh, texture: np.ndarray) -> np.ndarray:
    """メッシュの三角形のうち、テクスチャが描かれている（不透明な）ものの印。texture は RGBA の画像。"""
    alpha = texture[..., 3].astype(np.float64) / 255
    height, width = alpha.shape
    uvs = np.asarray(mesh.uvs, dtype=np.float64)
    triangles = np.asarray(mesh.triangles, dtype=np.int64).reshape(-1, 3)
    corners = uvs[triangles]
    samples = np.concatenate([corners, corners.mean(axis=1, keepdims=True)], axis=1)
    xs = np.clip((samples[..., 0] * width).astype(np.int64), 0, width - 1)
    ys = np.clip((samples[..., 1] * height).astype(np.int64), 0, height - 1)
    return alpha[ys, xs].mean(axis=1) >= OPAQUE_ALPHA
