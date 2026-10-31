"""頭の範囲のマスクと、頭だけを戻す処理のテスト。"""

from __future__ import annotations

from pathlib import Path

import numpy as np
from conftest import HEIGHT, WIDTH

from ugokue_pipeline.head import head_mask, outside_difference, paste_head
from ugokue_pipeline.result import load_result


def test_mask_covers_head_layers_but_not_the_body(result_dir: Path) -> None:
    result = load_result(result_dir)
    # 体の層（topwear）を足し、頭の層と重ならない場所に置く。
    from conftest import solid

    from ugokue_pipeline.result import Layer

    result.layers.append(Layer("topwear", (0, 46, 64, 48), 0.95, solid((0, 46, 64, 48), (9, 9, 9))))

    mask = head_mask(result, margin=0, feather=0)

    assert mask.shape == (HEIGHT, WIDTH)
    assert mask[20, 30] == 1  # 顔
    assert mask[47, 2] == 0  # 体だけの場所
    assert mask[0, 0] == 0  # 背景


def test_margin_grows_the_mask(result_dir: Path) -> None:
    result = load_result(result_dir)

    tight = head_mask(result, margin=0, feather=0)
    grown = head_mask(result, margin=2, feather=0)

    assert grown.sum() > tight.sum()
    assert np.all(grown[tight > 0] == 1)


def test_paste_keeps_pixels_outside_the_head() -> None:
    source = np.zeros((4, 4, 3), dtype=np.uint8)
    generated = np.full((4, 4, 3), 200, dtype=np.uint8)
    mask = np.zeros((4, 4), dtype=np.float32)
    mask[0, 0] = 1
    mask[0, 1] = 0.5

    pasted = paste_head(source, generated, mask)

    assert tuple(pasted[0, 0]) == (200, 200, 200)
    assert tuple(pasted[0, 1]) == (100, 100, 100)
    assert tuple(pasted[3, 3]) == (0, 0, 0)


def test_outside_difference_ignores_the_head() -> None:
    source = np.zeros((4, 4, 3), dtype=np.uint8)
    generated = source.copy()
    generated[0, :] = 255
    generated[3, 3] = 40
    mask = np.zeros((4, 4), dtype=np.float32)
    mask[0, :] = 1

    difference = outside_difference(source, generated, mask)

    assert difference["area"] == 12 / 16
    assert difference["mismatchRatio"] == 1 / 12
    assert difference["meanAbsDiff"] == 40 / 12


def test_mask_excludes_head_layers_hidden_behind_the_body(result_dir: Path) -> None:
    # 後ろ髪の層は、体の裏に隠れた部分まで補完されている。体の層が手前にあれば、そこは頭の範囲に入れない。
    from conftest import solid

    from ugokue_pipeline.result import Layer

    result = load_result(result_dir)
    result.layers.append(Layer("topwear", (0, 36, 64, 48), 0.3, solid((0, 36, 64, 48), (9, 9, 9))))

    mask = head_mask(result, margin=0, feather=0)

    assert mask[40, 10] == 0  # 後ろ髪の上に体がある
    assert mask[30, 10] == 1  # 体より上の後ろ髪


def test_crop_surrounds_the_face_with_room_for_hair_and_neck() -> None:
    from conftest import solid

    from ugokue_pipeline.head import head_crop
    from ugokue_pipeline.result import DecompositionResult, Layer

    face = Layer("face", (80, 50, 120, 100), 0.5, solid((80, 50, 120, 100), (250, 220, 200)))
    result = DecompositionResult(200, 300, [face], None)

    # 顔は幅 40、高さ 50。左右に 0.5 倍、上に 0.6 倍、下に 0.3 倍広げる。
    assert head_crop(result) == (60, 20, 140, 115)


def test_crop_stays_inside_the_image_and_needs_a_face(result_dir: Path) -> None:
    from ugokue_pipeline.head import head_crop

    result = load_result(result_dir)
    assert head_crop(result) == (0, 0, WIDTH, HEIGHT)

    result.layers = [x for x in result.layers if x.tag != "face"]
    import pytest

    with pytest.raises(ValueError):
        head_crop(result)


def test_mask_stops_a_little_below_the_chin() -> None:
    # 肩より下に垂れた髪は、頭の向きでは変えない。
    from conftest import solid

    from ugokue_pipeline.result import DecompositionResult, Layer

    face = Layer("face", (80, 50, 120, 100), 0.5, solid((80, 50, 120, 100), (250, 220, 200)))
    hair = Layer("front hair", (60, 40, 140, 280), 0.4, solid((60, 40, 140, 280), (20, 20, 20)))
    result = DecompositionResult(200, 300, [face, hair], None)

    mask = head_mask(result, margin=0, feather=0)

    # 顔の下端（100）から、顔の高さの 0.15 倍（7.5）まで。
    assert mask[107, 70] == 1
    assert mask[108, 70] == 0
    assert mask[250, 70] == 0
