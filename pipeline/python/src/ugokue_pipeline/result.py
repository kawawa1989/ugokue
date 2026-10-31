"""分解結果のフォルダの読み書き。バックエンド（Modal、手元の GPU、ComfyUI）によらず同じ形にする。"""

from __future__ import annotations

import json
import math
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image

INDEX_FILE = "layers.json"
RUN_FILE = "run.json"
SOURCE_FILE = "source.png"
LAYER_DIR = "layers"

# See-through が古い名前で書き出すタグを、今の名前にそろえる。
SEE_THROUGH_TAG_ALIASES = {"hairf": "front hair", "hairb": "back hair", "eyebg": "eyewhite"}


@dataclass
class Layer:
    """分解した層1枚。画像は箱の大きさに切り抜いた RGBA。"""

    tag: str
    box: tuple[int, int, int, int]
    depth_median: float
    image: np.ndarray
    depth: np.ndarray | None = None


@dataclass
class DecompositionResult:
    """分解結果のフォルダの中身。"""

    width: int
    height: int
    layers: list[Layer]
    source: np.ndarray | None
    run: dict[str, Any] = field(default_factory=dict)


def file_stem(tag: str) -> str:
    """タグを、ファイル名に使える形にする（英数字と - 以外は _ にする）。"""
    return re.sub(r"[^A-Za-z0-9-]+", "_", tag).strip("_") or "layer"


def write_result(
    directory: str | Path,
    width: int,
    height: int,
    layers: list[Layer],
    source: np.ndarray | None,
    run: dict[str, Any],
) -> None:
    """分解結果をフォルダに書き出す。"""
    directory = Path(directory)
    (directory / LAYER_DIR).mkdir(parents=True, exist_ok=True)
    entries = []
    for layer in layers:
        stem = file_stem(layer.tag)
        entry: dict[str, Any] = {
            "tag": layer.tag,
            "file": f"{LAYER_DIR}/{stem}.png",
            "box": list(layer.box),
            "depthMedian": layer.depth_median,
        }
        Image.fromarray(layer.image, "RGBA").save(directory / entry["file"])
        if layer.depth is not None:
            entry["depthFile"] = f"{LAYER_DIR}/{stem}_depth.png"
            Image.fromarray(layer.depth, "L").save(directory / entry["depthFile"])
        entries.append(entry)
    index = {"frameSize": {"width": width, "height": height}, "layers": entries}
    (directory / INDEX_FILE).write_text(json.dumps(index, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (directory / RUN_FILE).write_text(json.dumps(run, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if source is not None:
        Image.fromarray(source).save(directory / SOURCE_FILE)


def load_result(directory: str | Path) -> DecompositionResult:
    """分解結果のフォルダを読む。"""
    directory = Path(directory)
    index = json.loads((directory / INDEX_FILE).read_text(encoding="utf-8"))
    layers = []
    for entry in index["layers"]:
        image = np.array(Image.open(directory / entry["file"]).convert("RGBA"))
        depth = None
        if "depthFile" in entry:
            depth = np.array(Image.open(directory / entry["depthFile"]).convert("L"))
        box = tuple(int(v) for v in entry["box"])
        layers.append(Layer(entry["tag"], box, float(entry["depthMedian"]), image, depth))
    source_path = directory / SOURCE_FILE
    source = np.array(Image.open(source_path).convert("RGB")) if source_path.exists() else None
    run_path = directory / RUN_FILE
    run = json.loads(run_path.read_text(encoding="utf-8")) if run_path.exists() else {}
    size = index["frameSize"]
    return DecompositionResult(size["width"], size["height"], layers, source, run)


def from_see_through(optimized_dir: str | Path, source_path: str | Path, out_dir: str | Path, run: dict[str, Any]) -> None:
    """See-through の further_extr が書き出したフォルダ（optimized）を、分解結果のフォルダにする。"""
    optimized_dir = Path(optimized_dir)
    info = json.loads((optimized_dir / "info.json").read_text(encoding="utf-8"))
    height, width = (int(v) for v in info["frame_size"])
    layers = []
    for name, part in info["parts"].items():
        image_path = optimized_dir / f"{name}.png"
        if not image_path.exists():
            continue
        depth_path = optimized_dir / f"{name}_depth.png"
        depth = np.array(Image.open(depth_path).convert("L")) if depth_path.exists() else None
        x1, y1, x2, y2 = (int(v) for v in part["xyxy"])
        tag = SEE_THROUGH_TAG_ALIASES.get(name, name)
        layers.append(Layer(tag, (x1, y1, x2, y2), float(part["depth_median"]), np.array(Image.open(image_path).convert("RGBA")), depth))
    source = np.array(Image.open(source_path).convert("RGB"))
    result = to_source_frame(DecompositionResult(width, height, layers, source, run))
    write_result(out_dir, result.width, result.height, result.layers, result.source, result.run)


def to_source_frame(result: DecompositionResult) -> DecompositionResult:
    """See-through の正方形の座標（イラストの上下か左右に余白を足して拡大したもの）の層を、元のイラストの座標に戻す。"""
    if result.source is None:
        return result
    source_height, source_width = result.source.shape[:2]
    if (result.width, result.height) == (source_width, source_height):
        return result
    if result.width != result.height:
        raise ValueError(f"frame {result.width}x{result.height} is not square and differs from source {source_width}x{source_height}")
    side = max(source_width, source_height)
    scale = side / result.width
    pad_x = (side - source_width) // 2
    pad_y = (side - source_height) // 2
    layers = []
    for layer in result.layers:
        x1, y1, x2, y2 = layer.box
        sx1, sy1 = math.floor(x1 * scale) - pad_x, math.floor(y1 * scale) - pad_y
        sx2, sy2 = math.ceil(x2 * scale) - pad_x, math.ceil(y2 * scale) - pad_y
        cx1, cy1 = max(sx1, 0), max(sy1, 0)
        cx2, cy2 = min(sx2, source_width), min(sy2, source_height)
        if cx2 <= cx1 or cy2 <= cy1:
            continue
        size = (sx2 - sx1, sy2 - sy1)
        crop = (slice(cy1 - sy1, cy2 - sy1), slice(cx1 - sx1, cx2 - sx1))
        image = np.array(Image.fromarray(layer.image, "RGBA").resize(size, Image.Resampling.LANCZOS))[crop]
        depth = None
        if layer.depth is not None:
            depth = np.array(Image.fromarray(layer.depth, "L").resize(size, Image.Resampling.BILINEAR))[crop]
        layers.append(Layer(layer.tag, (cx1, cy1, cx2, cy2), layer.depth_median, image, depth))
    return DecompositionResult(source_width, source_height, layers, result.source, result.run)
