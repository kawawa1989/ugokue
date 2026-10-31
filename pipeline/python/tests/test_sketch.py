"""レビュー役が描く修正の線の確認、格子の絵、今の輪郭とのずれのテスト。"""

from __future__ import annotations

import numpy as np
import pytest
from PIL import Image

from ugokue_pipeline.review import REVIEW_X, REVIEW_Y
from ugokue_pipeline.sketch import GRID_HEIGHT, contour_distance, densify, face_contour, grid_image, validate_sketches

ANGLES = {(x, y) for x in REVIEW_X for y in REVIEW_Y}


def _sketch(**overrides) -> dict:
    sketch = {"angleX": 45, "angleY": 0, "kind": "contour", "label": "奥側の頬", "points": [[100, 200], [120, 260], [110, 320]], "note": "頬骨の下で外へ張る"}
    sketch.update(overrides)
    return sketch


def test_valid_sketches_pass_and_missing_sketches_are_allowed() -> None:
    assert validate_sketches(None, ANGLES) == []
    assert validate_sketches([_sketch(), _sketch(kind="other", angleX=-45, angleY=20)], ANGLES) == []


@pytest.mark.parametrize(
    "sketch, message",
    [
        (_sketch(angleX=30), "review angles"),
        (_sketch(kind="line"), "kind must be"),
        (_sketch(label=""), "label must be"),
        (_sketch(note=" "), "note must be"),
        (_sketch(points=[[1, 2]]), "two or more"),
        (_sketch(points=[[1, 2], [3, float("nan")]]), "finite"),
        (_sketch(points=[[1, 2], [3, True]]), "finite"),
        (_sketch(points=[[1, 2, 3], [3, 4]]), "two or more"),
    ],
)
def test_invalid_sketches_are_rejected(sketch: dict, message: str) -> None:
    assert any(message in issue for issue in validate_sketches([sketch], ANGLES))


def test_sketches_must_be_a_list() -> None:
    assert validate_sketches({"angleX": 45}, ANGLES) == ["sketches must be a list"]


def test_contour_distance_is_zero_on_the_contour_and_grows_with_the_offset() -> None:
    mask = np.zeros((200, 200), np.uint8)
    mask[50:150, 60:140] = 1
    contour = face_contour(mask)
    assert contour[:, 0].max() == 139 and contour[:, 1].min() == 50

    on_edge = np.array([[139.0, 60.0], [139.0, 140.0]])
    outside = on_edge + [10.0, 0.0]

    assert contour_distance(on_edge, contour) == {"mean": 0.0, "max": 0.0}
    assert contour_distance(outside, contour)["mean"] == pytest.approx(10.0)
    assert contour_distance(on_edge, np.zeros((0, 2))) is None
    assert len(densify(on_edge)) == 41


def test_grid_image_is_scaled_to_the_grid_height_and_draws_lines_at_canvas_coordinates() -> None:
    image = Image.new("RGB", (400, 400), (255, 255, 255))

    grid = grid_image(image, (25, 30, 325, 330))

    assert grid.height == GRID_HEIGHT and grid.width == GRID_HEIGHT
    scale = GRID_HEIGHT / 300
    line_x = round((100 - 25) * scale)
    assert np.array(grid)[GRID_HEIGHT // 2, line_x].tolist() != [255, 255, 255]
    assert np.array(grid)[GRID_HEIGHT // 2, line_x + 20].tolist() == [255, 255, 255]
