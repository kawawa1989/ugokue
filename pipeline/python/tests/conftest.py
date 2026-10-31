"""テストで使う、小さな分解結果を作る。"""

from __future__ import annotations

from pathlib import Path

import numpy as np
import pytest

from ugokue_pipeline.result import Layer, write_result

WIDTH = 64
HEIGHT = 48

# (タグ, 箱 [x1, y1, x2, y2], 深度の中央値, 色)。深度は小さいほど手前。
LAYERS = [
    ("back hair", (8, 2, 56, 46), 0.9, (60, 40, 30)),
    ("ears-r", (10, 18, 16, 28), 0.71, (230, 190, 170)),
    ("face", (14, 8, 50, 44), 0.7, (250, 220, 200)),
    ("eyewhite-r", (20, 20, 28, 26), 0.65, (255, 255, 255)),
    ("eyewhite-l", (36, 20, 44, 26), 0.65, (255, 255, 255)),
    ("irides-r", (22, 20, 26, 26), 0.6, (40, 60, 160)),
    ("irides-l", (38, 20, 42, 26), 0.6, (40, 60, 160)),
    ("mouth", (28, 34, 36, 37), 0.62, (180, 60, 70)),
    ("front hair", (12, 2, 52, 18), 0.4, (80, 50, 35)),
    ("head", (8, 2, 56, 46), 0.5, (1, 2, 3)),
]


def solid(box: tuple[int, int, int, int], color: tuple[int, int, int], alpha: int = 255) -> np.ndarray:
    """箱の大きさの単色の RGBA 画像を作る。"""
    x1, y1, x2, y2 = box
    image = np.zeros((y2 - y1, x2 - x1, 4), dtype=np.uint8)
    image[..., :3] = color
    image[..., 3] = alpha
    return image


def make_layers() -> list[Layer]:
    """LAYERS の層を作る。"""
    return [Layer(tag, box, depth, solid(box, color)) for tag, box, depth, color in LAYERS]


def flatten(layers: list[Layer], background: tuple[int, int, int] = (200, 200, 200)) -> np.ndarray:
    """層を奥から順に重ねた、背景付きの RGB 画像を作る（元のイラストの代わり）。"""
    image = np.zeros((HEIGHT, WIDTH, 3), dtype=np.uint8)
    image[...] = background
    for layer in sorted((x for x in layers if x.tag != "head"), key=lambda x: -x.depth_median):
        x1, y1, x2, y2 = layer.box
        alpha = layer.image[..., 3:4] / 255.0
        image[y1:y2, x1:x2] = (layer.image[..., :3] * alpha + image[y1:y2, x1:x2] * (1 - alpha)).round().astype(np.uint8)
    return image


@pytest.fixture
def result_dir(tmp_path: Path) -> Path:
    """LAYERS の分解結果のフォルダ。"""
    layers = make_layers()
    directory = tmp_path / "result"
    write_result(directory, WIDTH, HEIGHT, layers, flatten(layers), {"backend": "test", "seed": 42})
    return directory
