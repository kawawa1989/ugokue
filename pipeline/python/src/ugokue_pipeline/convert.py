"""分解結果を、中間形式 v0 のモデル（パーツと四角形のメッシュ）に変換する。"""

from __future__ import annotations

import io
import re
from pathlib import Path

from PIL import Image

from ugokue_format.model import Canvas, Correspondence, Deformation, Mesh, Model, Parameter, Part, View
from ugokue_format.package import FORMAT_NAME, FORMAT_VERSION, ModelPackage, save

from .result import DecompositionResult, Layer, load_result

# See-through のタグ → (パーツ ID, 役割, 親)。左右に分けたタグは -r（キャラクターの右、画像の左）と -l を付ける。
_RULES: dict[str, tuple[str, str, str]] = {
    "front hair": ("hair_front", "hair_front", "head"),
    "back hair": ("hair_back", "hair_back", "head"),
    "hair": ("hair", "hair_front", "head"),
    "face": ("face", "face", "head"),
    "eyewhite": ("eye_white", "eye_white", "face"),
    "irides": ("iris", "iris", "face"),
    "eyelash": ("eyelid", "eyelid", "face"),
    "eyebrow": ("eyebrow", "eyebrow", "face"),
    "ears": ("ear", "ear", "head"),
    "nose": ("nose", "nose", "face"),
    "mouth": ("mouth", "mouth", "face"),
    "headwear": ("headwear", "accessory", "head"),
    "eyewear": ("eyewear", "accessory", "face"),
    "earwear": ("earwear", "accessory", "head"),
    "neck": ("neck", "neck", "body"),
    "neckwear": ("neckwear", "clothes", "body"),
    "topwear": ("topwear", "clothes", "body"),
    "handwear": ("handwear", "clothes", "body"),
    "bottomwear": ("bottomwear", "clothes", "body"),
    "legwear": ("legwear", "clothes", "body"),
    "footwear": ("footwear", "clothes", "body"),
}

# 合成済みの頭の層。個別のパーツと重なるので使わない。
_SKIPPED_TAGS = {"head"}

STANDARD_PARAMETERS = [
    Parameter("AngleX", -30, 30, 0),
    Parameter("AngleY", -30, 30, 0),
    Parameter("AngleZ", -30, 30, 0),
    Parameter("EyeOpenL", 0, 1, 1),
    Parameter("EyeOpenR", 0, 1, 1),
    Parameter("MouthOpenY", 0, 1, 0),
    Parameter("Breath", 0, 1, 0),
]


def part_rule(tag: str) -> tuple[str, str, str] | None:
    """タグからパーツ ID、役割、親を決める。使わない層なら None を返す。"""
    if tag in _SKIPPED_TAGS:
        return None
    base, side = tag, ""
    match = re.fullmatch(r"(.+)-([lr])", tag)
    if match and match.group(1) in _RULES:
        base, side = match.group(1), "_" + match.group(2)
    if base in _RULES:
        part_id, role, parent = _RULES[base]
        return part_id + side, role, parent
    return re.sub(r"[^a-z0-9]+", "_", tag.lower()).strip("_") or "layer", "accessory", "body"


def _png(layer: Layer) -> bytes:
    buffer = io.BytesIO()
    Image.fromarray(layer.image, "RGBA").save(buffer, format="PNG")
    return buffer.getvalue()


def _center(box: tuple[int, int, int, int]) -> list[float]:
    x1, y1, x2, y2 = box
    return [(x1 + x2) / 2, (y1 + y2) / 2]


def to_model(result: DecompositionResult) -> ModelPackage:
    """分解結果の層を、奥から順に描画順を付けたパーツにする。各パーツには箱の大きさの四角形のメッシュを付ける。"""
    entries = []
    for layer in result.layers:
        rule = part_rule(layer.tag)
        if rule is not None:
            entries.append((rule, layer))
    entries.sort(key=lambda e: (-e[1].depth_median, e[0][0]))

    used: set[str] = set()
    parts: list[Part] = []
    meshes: list[Mesh] = []
    textures: dict[str, bytes] = {}
    boxes: dict[str, tuple[int, int, int, int]] = {}
    for order, ((part_id, role, parent), layer) in enumerate(entries, start=1):
        unique = part_id
        suffix = 2
        while unique in used or unique in ("body", "head"):
            unique = f"{part_id}_{suffix}"
            suffix += 1
        used.add(unique)
        x1, y1, x2, y2 = layer.box
        texture = f"textures/{unique}.png"
        meshes.append(Mesh(unique, unique, texture, [[x1, y1], [x2, y1], [x2, y2], [x1, y2]], [[0, 0], [1, 0], [1, 1], [0, 1]], [[0, 1, 2], [0, 2, 3]]))
        parts.append(Part(unique, role, parent, _center(layer.box), order * 10, unique))
        textures[texture] = _png(layer)
        boxes[unique] = layer.box

    ids = {p.id for p in parts}
    for part in parts:
        if part.parent == "face" and "face" not in ids:
            part.parent = "head"
        if part.role == "iris":
            white = part.id.replace("iris", "eye_white", 1)
            if white in ids:
                part.clip_by = [white]

    head_box = boxes.get("face") or boxes.get("hair_front") or (0, 0, result.width, result.height)
    body_box = boxes.get("neck") or boxes.get("topwear") or (0, 0, result.width, result.height)
    groups = [
        Part("body", "body", None, _center(body_box), 0, None),
        Part("head", "head", "body", _center(head_box), 0, None),
    ]
    model = Model(
        FORMAT_NAME,
        FORMAT_VERSION,
        Canvas(result.width, result.height),
        list(STANDARD_PARAMETERS),
        groups + parts,
        meshes,
        [View("front", 0, 0, [])],
        Correspondence([], []),
        Deformation([], []),
    )
    return ModelPackage(model, textures)


def convert(result_dir: str | Path, model_dir: str | Path) -> None:
    """分解結果のフォルダを読み、中間形式のモデルのフォルダに書き出す。"""
    save(to_model(load_result(result_dir)), model_dir)
