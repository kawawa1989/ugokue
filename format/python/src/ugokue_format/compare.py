"""2つのモデルのフォルダが同じ内容かを比べる。"""

from __future__ import annotations

from pathlib import Path
from typing import Any

from .package import MODEL_FILE, read_model_json
from .validate import PathPart, json_path


def _is_num(v: Any) -> bool:
    """JSON の数値（真偽値を除く）かどうかを返す。"""
    return isinstance(v, int | float) and not isinstance(v, bool)


def diff_json(a: Any, b: Any, path: tuple[PathPart, ...] = ()) -> list[str]:
    """2つの JSON 値の違いを JSON パス付きで返す。数値は 1 と 1.0 を等しいとみなす。"""
    where = json_path(path)
    if _is_num(a) and _is_num(b):
        return [] if a == b else [f"{where}: {a!r} != {b!r}"]
    if isinstance(a, dict) and isinstance(b, dict):
        out = [f"{json_path((*path, k))}: {'右にない' if k in a else '左にない'}" for k in sorted(a.keys() ^ b.keys())]
        for k in a:
            if k in b:
                out += diff_json(a[k], b[k], (*path, k))
        return out
    if isinstance(a, list) and isinstance(b, list):
        out = [] if len(a) == len(b) else [f"{where}: 要素数 {len(a)} != {len(b)}"]
        for i, (x, y) in enumerate(zip(a, b)):
            out += diff_json(x, y, (*path, i))
        return out
    if type(a) is not type(b) or a != b:
        return [f"{where}: {a!r} != {b!r}"]
    return []


def _files(directory: Path) -> dict[str, Path]:
    """model.json と隠しファイルを除く、フォルダ内のファイルを相対パスで返す。"""
    files = {}
    for p in directory.rglob("*"):
        rel = p.relative_to(directory).as_posix()
        if p.is_file() and rel != MODEL_FILE and not any(s.startswith(".") for s in rel.split("/")):
            files[rel] = p
    return files


def compare_dirs(a: str | Path, b: str | Path) -> list[str]:
    """model.json の値とテクスチャのバイト列を比べ、違いを返す。"""
    a, b = Path(a), Path(b)
    diffs = diff_json(read_model_json(a), read_model_json(b))
    files_a, files_b = _files(a), _files(b)
    diffs += [f"{rel}: 右にない" for rel in sorted(files_a.keys() - files_b.keys())]
    diffs += [f"{rel}: 左にない" for rel in sorted(files_b.keys() - files_a.keys())]
    diffs += [
        f"{rel}: バイト列が違う"
        for rel in sorted(files_a.keys() & files_b.keys())
        if files_a[rel].read_bytes() != files_b[rel].read_bytes()
    ]
    return diffs
