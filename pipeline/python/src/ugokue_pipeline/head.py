"""分解結果から頭の範囲のマスクを作り、生成した絵の頭だけを元のイラストに戻す。"""

from __future__ import annotations

import math

import numpy as np
from PIL import Image, ImageFilter

from .composite import MISMATCH_THRESHOLD
from .convert import ordered_layers
from .result import DecompositionResult

# 親がこれらのパーツを頭の範囲に含める（髪、顔、目、耳、頭の飾りなど。首と体は含めない）。
HEAD_PARENTS = {"head", "face"}

# 生成のために切り出す範囲を、顔の層の箱から広げる量（顔の幅・高さに対する割合）。上は髪、下は首と襟元の分。
CROP_SIDE = 0.5
CROP_TOP = 0.6
CROP_BOTTOM = 0.3
# 頭の範囲のマスクを、顔の下端からこの割合（顔の高さに対する）だけ下で切る。肩より下に垂れた髪は含めない。
MASK_BELOW_CHIN = 0.15


def head_crop(result: DecompositionResult) -> tuple[int, int, int, int]:
    """顔の層の箱を広げた、生成のために切り出す範囲 [x1, y1, x2, y2] を返す。体がほとんど入らないようにする。"""
    faces = [layer for layer in result.layers if layer.tag == "face"]
    if not faces:
        raise ValueError("no face layer to crop around")
    x1, y1, x2, y2 = faces[0].box
    width, height = x2 - x1, y2 - y1
    return (
        max(0, math.floor(x1 - width * CROP_SIDE)),
        max(0, math.floor(y1 - height * CROP_TOP)),
        min(result.width, math.ceil(x2 + width * CROP_SIDE)),
        min(result.height, math.ceil(y2 + height * CROP_BOTTOM)),
    )


def head_mask(result: DecompositionResult, margin: int, feather: float) -> np.ndarray:
    """モデルの描画順で重ねたとき、いちばん手前に見える層が頭の層である画素を集め、margin 画素広げ、feather 画素ぼかしたマスク（0〜1）を返す。
    体の裏に補完された後ろ髪のように隠れている部分と、あごより少し下から下は含めない。"""
    mask = np.zeros((result.height, result.width), dtype=np.uint8)
    for (_, _, parent), layer in ordered_layers(result):
        x1, y1, x2, y2 = layer.box
        opaque = layer.image[..., 3] >= 128
        region = mask[y1:y2, x1:x2]
        region[opaque] = 255 if parent in HEAD_PARENTS else 0
    faces = [layer for layer in result.layers if layer.tag == "face"]
    if faces:
        _, top, _, bottom = faces[0].box
        mask[math.floor(bottom + (bottom - top) * MASK_BELOW_CHIN) + 1 :] = 0
    image = Image.fromarray(mask, "L")
    for _ in range(margin):
        image = image.filter(ImageFilter.MaxFilter(3))
    if feather > 0:
        image = image.filter(ImageFilter.GaussianBlur(feather))
    return np.asarray(image, dtype=np.float32) / 255


def paste_head(source: np.ndarray, generated: np.ndarray, mask: np.ndarray) -> np.ndarray:
    """マスクの部分を生成した絵に、それ以外を元のイラストにした RGB 画像を返す。"""
    weight = mask[..., None]
    mixed = generated[..., :3].astype(np.float64) * weight + source[..., :3].astype(np.float64) * (1 - weight)
    return mixed.round().astype(np.uint8)


def outside_difference(source: np.ndarray, generated: np.ndarray, mask: np.ndarray) -> dict[str, float]:
    """頭の範囲の外（マスクが 0 の画素）で、生成した絵が元のイラストからどれだけ変わったかを返す。"""
    outside = mask <= 0
    diff = np.abs(generated[..., :3].astype(np.int16) - source[..., :3].astype(np.int16)).mean(axis=-1)
    values = diff[outside]
    if values.size == 0:
        return {"area": 0.0, "meanAbsDiff": 0.0, "mismatchRatio": 0.0}
    return {
        "area": float(outside.mean()),
        "meanAbsDiff": float(values.mean()),
        "mismatchRatio": float((values > MISMATCH_THRESHOLD).mean()),
    }
