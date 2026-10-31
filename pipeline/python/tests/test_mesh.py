"""層の画像から格子のメッシュを作る処理のテスト。"""

from __future__ import annotations

import numpy as np

from ugokue_pipeline.mesh import cell_size, grid_mesh


def test_grid_covers_the_box_and_maps_uvs_to_positions() -> None:
    alpha = np.full((20, 30), 255, dtype=np.uint8)

    vertices, uvs, triangles = grid_mesh(alpha, (100, 50, 130, 70), cell=10)

    assert len(vertices) == 4 * 3
    assert len(triangles) == 2 * 3 * 2
    assert min(v[0] for v in vertices) == 100 and max(v[0] for v in vertices) == 130
    assert min(v[1] for v in vertices) == 50 and max(v[1] for v in vertices) == 70
    for (x, y), (u, v) in zip(vertices, uvs):
        assert abs(u - (x - 100) / 30) < 1e-5 and abs(v - (y - 50) / 20) < 1e-5


def test_cells_without_opaque_pixels_are_dropped() -> None:
    alpha = np.zeros((20, 30), dtype=np.uint8)
    alpha[:10, :10] = 255

    vertices, _, triangles = grid_mesh(alpha, (0, 0, 30, 20), cell=10)

    assert len(triangles) == 2
    assert sorted(map(tuple, vertices)) == [(0, 0), (0, 10), (10, 0), (10, 10)]


def test_triangles_wind_like_the_test_model() -> None:
    # 固定テストモデルと同じ巻き方（キャンバス座標の外積が正）にそろえる。
    alpha = np.full((10, 10), 255, dtype=np.uint8)
    vertices, _, triangles = grid_mesh(alpha, (0, 0, 10, 10), cell=10)

    for i, j, k in triangles:
        (ax, ay), (bx, by), (cx, cy) = vertices[i], vertices[j], vertices[k]
        assert (bx - ax) * (cy - ay) - (by - ay) * (cx - ax) > 0


def test_empty_layer_still_gets_one_cell() -> None:
    vertices, _, triangles = grid_mesh(np.zeros((5, 5), dtype=np.uint8), (0, 0, 5, 5), cell=10)

    assert len(vertices) == 4 and len(triangles) == 2


def test_cell_size_scales_with_the_canvas() -> None:
    assert cell_size(896, 1152) == 19
    assert cell_size(64, 48) == 8
