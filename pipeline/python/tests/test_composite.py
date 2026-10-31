"""パーツを重ねた画像と元のイラストの比較のテスト。"""

from __future__ import annotations

from pathlib import Path

import numpy as np
from conftest import HEIGHT, WIDTH

from ugokue_pipeline.composite import compare, composite, diff_image
from ugokue_pipeline.result import load_result


def test_composite_of_all_parts_matches_the_source(result_dir: Path) -> None:
    result = load_result(result_dir)

    image = composite(result)
    comparison = compare(image, result.source)

    assert image.shape == (HEIGHT, WIDTH, 4)
    assert comparison.mean_abs_diff == 0
    assert comparison.p95_abs_diff == 0
    assert comparison.mismatch_ratio == 0
    assert 0.5 < comparison.coverage < 1


def test_head_layer_is_not_composited(result_dir: Path) -> None:
    result = load_result(result_dir)

    image = composite(result)

    assert not np.any(np.all(image[..., :3] == (1, 2, 3), axis=-1))


def test_composite_follows_the_model_draw_order(result_dir: Path) -> None:
    result = load_result(result_dir)
    for layer in result.layers:
        if layer.tag == "front hair":
            layer.depth_median = 0.8

    image = composite(result)

    # 前髪と顔が重なる画素では、前髪の色になる（変換の描画順と同じ）。
    assert tuple(image[10, 30, :3]) == (80, 50, 35)


def test_comparison_detects_a_missing_part(result_dir: Path) -> None:
    result = load_result(result_dir)
    result.layers = [x for x in result.layers if x.tag != "irides-l"]

    comparison = compare(composite(result), result.source)

    assert comparison.mismatch_ratio > 0.005
    assert comparison.mean_abs_diff > 1


def test_diff_image_marks_differences(result_dir: Path) -> None:
    result = load_result(result_dir)
    result.layers = [x for x in result.layers if x.tag != "mouth"]

    diff = diff_image(composite(result), result.source)

    assert diff.shape == (HEIGHT, WIDTH, 3)
    assert diff[35, 32].max() > 100
    assert diff[2, 2].max() == 0
