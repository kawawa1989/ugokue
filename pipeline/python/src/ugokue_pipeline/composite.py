"""分解結果のパーツを重ねた画像と、元のイラストの比較。"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from .convert import ordered_layers
from .result import DecompositionResult

# 1画素の色の差（RGB の平均、0〜255）がこれを超えたら、一致しない画素とみなす。
MISMATCH_THRESHOLD = 32


@dataclass
class Comparison:
    """重ねた画像と元のイラストの違い。比べるのは、重ねた画像の不透明度が半分以上の画素だけ。"""

    mean_abs_diff: float
    p95_abs_diff: float
    mismatch_ratio: float
    coverage: float


def composite(result: DecompositionResult) -> np.ndarray:
    """変換に使う層を、モデルの描画順（奥から手前）にアルファ合成した RGBA 画像を返す。"""
    color = np.zeros((result.height, result.width, 3), dtype=np.float64)
    alpha = np.zeros((result.height, result.width, 1), dtype=np.float64)
    for _, layer in ordered_layers(result):
        x1, y1, x2, y2 = layer.box
        top = layer.image.astype(np.float64) / 255
        a = top[..., 3:4]
        region_color = color[y1:y2, x1:x2]
        region_alpha = alpha[y1:y2, x1:x2]
        out_alpha = a + region_alpha * (1 - a)
        premultiplied = top[..., :3] * a + region_color * region_alpha * (1 - a)
        with np.errstate(invalid="ignore", divide="ignore"):
            color[y1:y2, x1:x2] = np.where(out_alpha > 0, premultiplied / out_alpha, 0)
        alpha[y1:y2, x1:x2] = out_alpha
    return (np.concatenate([color, alpha], axis=-1) * 255).round().astype(np.uint8)


def _abs_diff(image: np.ndarray, source: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    region = image[..., 3] >= 128
    diff = np.abs(image[..., :3].astype(np.int16) - source[..., :3].astype(np.int16)).mean(axis=-1)
    return diff, region


def compare(image: np.ndarray, source: np.ndarray) -> Comparison:
    """重ねた画像（RGBA）と元のイラスト（RGB）を比べる。"""
    diff, region = _abs_diff(image, source)
    values = diff[region]
    if values.size == 0:
        return Comparison(0, 0, 0, 0)
    return Comparison(
        float(values.mean()),
        float(np.percentile(values, 95)),
        float((values > MISMATCH_THRESHOLD).mean()),
        float(region.mean()),
    )


def diff_image(image: np.ndarray, source: np.ndarray) -> np.ndarray:
    """比べた画素の色の差の絶対値を RGB 画像にする。比べない画素は黒にする。"""
    difference = np.abs(image[..., :3].astype(np.int16) - source[..., :3].astype(np.int16)).astype(np.uint8)
    difference[image[..., 3] < 128] = 0
    return difference
