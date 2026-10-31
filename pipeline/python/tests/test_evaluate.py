"""キーフォームの評価（特徴点のずれ、画像の差、三角形の品質）のテスト。"""

from __future__ import annotations

import numpy as np

from ugokue_format.model import Canvas, Correspondence, Deformation, Keyform, KeyformGroup, Mesh, Model, Part, View
from ugokue_pipeline.evaluate import image_difference, landmark_error, locate, mesh_quality, move_point

MESH = Mesh("m", "p", "t.png", [[0, 0], [10, 0], [10, 10], [0, 10]], [[0, 0], [1, 0], [1, 1], [0, 1]], [[0, 1, 2], [0, 2, 3]])


def _model(offsets: dict[tuple[int, int], list[list[float]]]) -> Model:
    keyforms = [Keyform([x, y], offsets.get((x, y), [[0, 0]] * 4)) for x in (-30, 0, 30) for y in (-20, 0, 20)]
    group = KeyformGroup("g", "m", ["AngleX", "AngleY"], [[-30, 0, 30], [-20, 0, 20]], "linear", keyforms)
    return Model("ugokue", "0.1.0", Canvas(20, 20), [], [Part("p", "face", None, [5, 5], 1, "m")], [MESH], [View("front", 0, 0, [])], Correspondence(), Deformation([group], []))


def test_locate_and_move_a_point_with_its_triangle() -> None:
    model = _model({(30, 0): [[2, 1]] * 4})

    assert locate(MESH, (8, 2))[0] == 0
    assert locate(MESH, (30, 30)) is None
    assert np.allclose(move_point(MESH, model.deformation.keyform_groups, (8, 2), {"AngleX": 30}), [10, 3])


def test_landmark_error_before_and_after_deforming() -> None:
    model = _model({(30, 0): [[2, 0]] * 4})

    error = landmark_error(model, {"a": "m", "b": "m"}, {"a": (2, 2), "b": (7, 7)}, {"a": (4, 2), "b": (9, 7)}, {"AngleX": 30})

    assert error == {"count": 2, "before": 2.0, "after": 0.0}


def test_image_difference_counts_only_the_region() -> None:
    rendered = np.zeros((4, 4, 3), dtype=np.uint8)
    target = rendered.copy()
    target[0, 0] = 100
    target[3, 3] = 100
    region = np.zeros((4, 4), dtype=bool)
    region[:2, :2] = True

    assert image_difference(rendered, target, region) == {"meanAbsDiff": 25.0, "mismatchRatio": 0.25}


def test_mesh_quality_finds_flips_between_grid_points() -> None:
    smooth = mesh_quality(_model({(30, 0): [[1, 0]] * 4}), ["m"], [-30, 0, 15, 30], [0])
    assert smooth["flippedTriangles"] == 0 and smooth["meshesWithProblems"] == {}

    # 右を向くと1つの頂点が三角形の反対側へ抜ける。
    flips = mesh_quality(_model({(30, 0): [[0, 0], [0, 0], [-25, -25], [0, 0]]}), ["m"], [0, 15, 30], [0])
    assert flips["flippedTriangles"] >= 1 and flips["meshesWithProblems"]["m"] >= 1
