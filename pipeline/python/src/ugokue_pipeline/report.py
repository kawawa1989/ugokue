"""分解結果を変換・検証・比較し、結果をまとめる。"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image

from ugokue_format.validate import validate_dir

from .composite import compare, composite, diff_image
from .convert import convert
from .result import load_result


def write_report(out_dir: Path, items: list[Path]) -> dict[str, Any]:
    """各フォルダの result を model に変換して検証し、重ねた画像と元のイラストを比べて summary.json に書く。"""
    results = []
    for item in items:
        result = load_result(item / "result")
        convert(item / "result", item / "model")
        issues = [str(x) for x in validate_dir(item / "model")]
        image = composite(result)
        Image.fromarray(image, "RGBA").save(item / "composite.png")
        entry: dict[str, Any] = {
            "name": item.name,
            "validationIssues": issues,
            "parts": sum(1 for _ in (item / "model" / "textures").glob("*.png")),
            "tags": [x.tag for x in result.layers],
            "run": result.run,
        }
        if result.source is not None:
            comparison = compare(image, result.source)
            Image.fromarray(diff_image(image, result.source)).save(item / "diff.png")
            entry["comparison"] = {
                "meanAbsDiff": round(comparison.mean_abs_diff, 3),
                "p95AbsDiff": round(comparison.p95_abs_diff, 3),
                "mismatchRatio": round(comparison.mismatch_ratio, 5),
                "coverage": round(comparison.coverage, 4),
            }
        results.append(entry)
    summary = {"results": results}
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return summary


def compare_results(a: Path, b: Path) -> dict[str, Any]:
    """2つの分解結果の層を画素単位で比べる。同じタグの層の箱と画素の違いを返す。"""
    first = {x.tag: x for x in load_result(a).layers}
    second = {x.tag: x for x in load_result(b).layers}
    layers = {}
    max_diff = 0
    for tag in sorted(first.keys() & second.keys()):
        x, y = first[tag], second[tag]
        if x.box != y.box or x.image.shape != y.image.shape:
            layers[tag] = {"sameBox": False}
            max_diff = 255
            continue
        diff = np.abs(x.image.astype(np.int16) - y.image.astype(np.int16)).max(axis=-1)
        layers[tag] = {"sameBox": True, "maxAbsDiff": int(diff.max()), "differentPixels": int((diff > 0).sum())}
        max_diff = max(max_diff, int(diff.max()))
    only = sorted(first.keys() ^ second.keys())
    return {"identical": max_diff == 0 and not only, "maxAbsDiff": max_diff, "tagsOnlyInOne": only, "layers": layers}
