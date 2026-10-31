"""層の画像から、不透明な部分を覆う格子のメッシュを作る。"""

from __future__ import annotations

import math

import numpy as np

# 格子の1マスの大きさは、キャンバスの短い辺のこの割合（画素）。小さすぎないよう下限を置く。
CELL_RATIO = 1 / 48
MIN_CELL = 8


def cell_size(width: int, height: int) -> int:
    """キャンバスの大きさから、格子の1マスの大きさ（画素）を決める。"""
    return max(MIN_CELL, round(min(width, height) * CELL_RATIO))


def grid_mesh(alpha: np.ndarray, box: tuple[int, int, int, int], cell: int) -> tuple[list[list[float]], list[list[float]], list[list[int]]]:
    """箱を約 cell 画素の格子に分け、不透明な画素を含むマスだけを残したメッシュの頂点（キャンバス座標）、UV、三角形を返す。"""
    x1, y1, x2, y2 = box
    width, height = x2 - x1, y2 - y1
    cols, rows = max(1, math.ceil(width / cell)), max(1, math.ceil(height / cell))
    kept = []
    for j in range(rows):
        for i in range(cols):
            top, bottom = math.floor(j * height / rows), math.ceil((j + 1) * height / rows)
            left, right = math.floor(i * width / cols), math.ceil((i + 1) * width / cols)
            if np.any(alpha[top:bottom, left:right] > 0):
                kept.append((i, j))
    if not kept:
        kept = [(i, j) for j in range(rows) for i in range(cols)][:1]

    index: dict[tuple[int, int], int] = {}
    vertices: list[list[float]] = []
    uvs: list[list[float]] = []

    def vertex(i: int, j: int) -> int:
        if (i, j) not in index:
            u, v = i / cols, j / rows
            index[(i, j)] = len(vertices)
            vertices.append([round(x1 + width * u, 3), round(y1 + height * v, 3)])
            uvs.append([round(u, 6), round(v, 6)])
        return index[(i, j)]

    triangles = []
    for i, j in kept:
        a, b, c, d = vertex(i, j), vertex(i + 1, j), vertex(i + 1, j + 1), vertex(i, j + 1)
        triangles += [[a, b, c], [a, c, d]]
    return vertices, uvs, triangles
