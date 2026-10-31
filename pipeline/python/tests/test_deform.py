"""キーフォームの補間と、三角形の裏返り・伸びの検出のテスト。"""

from __future__ import annotations

import numpy as np

from ugokue_format.model import Keyform, KeyformGroup, Mesh
from ugokue_pipeline.deform import deformed_positions, triangle_quality

MESH = Mesh("m", "p", "t.png", [[0, 0], [10, 0], [10, 10], [0, 10]], [[0, 0], [1, 0], [1, 1], [0, 1]], [[0, 1, 2], [0, 2, 3]])


def _group() -> KeyformGroup:
    keyforms = []
    for x in (-30, 0, 30):
        for y in (-20, 0, 20):
            keyforms.append(Keyform([x, y], [[x / 10, y / 10]] * 4))
    return KeyformGroup("g", "m", ["AngleX", "AngleY"], [[-30, 0, 30], [-20, 0, 20]], "linear", keyforms)


def test_grid_points_use_their_keyform() -> None:
    positions = deformed_positions(MESH, [_group()], {"AngleX": 30, "AngleY": -20})

    assert np.allclose(positions, np.array(MESH.vertices) + [3, -2])


def test_between_grid_points_is_linear_and_clamped() -> None:
    assert np.allclose(deformed_positions(MESH, [_group()], {"AngleX": 15, "AngleY": 10}), np.array(MESH.vertices) + [1.5, 1])
    assert np.allclose(deformed_positions(MESH, [_group()], {"AngleX": 90}), np.array(MESH.vertices) + [3, 0])
    assert np.allclose(deformed_positions(MESH, [], {"AngleX": 30}), MESH.vertices)


def test_quality_detects_flipped_and_stretched_triangles() -> None:
    rest = np.array(MESH.vertices, dtype=float)

    same = triangle_quality(rest, rest, MESH.triangles)
    assert same["flipped"] == 0 and np.isclose(same["maxStretch"], 1) and np.isclose(same["minStretch"], 1)

    flipped = rest.copy()
    flipped[2] = [-10, -10]
    assert triangle_quality(rest, flipped, MESH.triangles)["flipped"] >= 1

    stretched = rest * [4, 1]
    quality = triangle_quality(rest, stretched, MESH.triangles)
    assert quality["flipped"] == 0 and np.isclose(quality["maxStretch"], 4)
