"""モデルのフォルダ（model.json とテクスチャ）の読み書き。"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from .model import Model

FORMAT_NAME = "ugokue-model"
FORMAT_VERSION = "0.1.0"
MODEL_FILE = "model.json"
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
_READABLE_VERSION = re.compile(r"^0\.1\.[0-9]+$")


class FormatError(Exception):
    """モデルのフォルダを読み書きできないことを表す。"""


@dataclass
class ModelPackage:
    """モデル本体と、相対パスをキーにしたテクスチャのバイト列。"""

    model: Model
    textures: dict[str, bytes] = field(default_factory=dict)


def is_readable_version(version: Any) -> bool:
    """この実装が読めるバージョン（0.1.x）かどうかを返す。"""
    return isinstance(version, str) and _READABLE_VERSION.match(version) is not None


def texture_path_problem(texture: str) -> str | None:
    """テクスチャの相対パスがフォルダの外を指すなど不正なら、その理由を返す。"""
    if not texture or "\\" in texture or texture.startswith("/") or re.match(r"^[A-Za-z]:", texture):
        return "テクスチャのパスは '/' 区切りの相対パスでなければならない"
    if any(s in ("", ".", "..") for s in texture.split("/")):
        return "テクスチャのパスはフォルダの外を指せない"
    return None


def read_model_json(directory: Path) -> Any:
    """フォルダの model.json を UTF-8（BOM なし）の JSON として読む。"""
    raw = (directory / MODEL_FILE).read_bytes()
    if raw.startswith(b"\xef\xbb\xbf"):
        raise FormatError("model.json に BOM がある")
    return json.loads(raw.decode("utf-8"))


def dump_model_json(data: dict[str, Any]) -> bytes:
    """model.json の正規の書き出し形式のバイト列にする。"""
    return (json.dumps(data, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def load(directory: str | Path) -> ModelPackage:
    """フォルダから model.json と参照されるテクスチャを読む。"""
    directory = Path(directory)
    try:
        data = read_model_json(directory)
    except (OSError, ValueError) as e:
        raise FormatError(f"{directory / MODEL_FILE} を読めない: {e}") from e
    if not isinstance(data, dict) or data.get("format") != FORMAT_NAME:
        raise FormatError(f"format が {FORMAT_NAME!r} ではない")
    if not is_readable_version(data.get("version")):
        raise FormatError(f"version {data.get('version')!r} は読めない（読めるのは 0.1.x）")
    try:
        model = Model.from_dict(data)
    except (KeyError, TypeError, IndexError) as e:
        raise FormatError(f"model.json の構造が不正: {e!r}") from e
    textures: dict[str, bytes] = {}
    for mesh in model.meshes:
        if mesh.texture in textures:
            continue
        if problem := texture_path_problem(mesh.texture):
            raise FormatError(f"{mesh.texture}: {problem}")
        try:
            textures[mesh.texture] = (directory / mesh.texture).read_bytes()
        except OSError as e:
            raise FormatError(f"テクスチャ {mesh.texture} を読めない: {e}") from e
    return ModelPackage(model, textures)


def save(package: ModelPackage, directory: str | Path) -> None:
    """model.json を正規の形式で、テクスチャをバイト列のまま書き出す。"""
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    (directory / MODEL_FILE).write_bytes(dump_model_json(package.model.to_dict()))
    for rel, data in sorted(package.textures.items()):
        if problem := texture_path_problem(rel):
            raise FormatError(f"{rel}: {problem}")
        path = directory / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
