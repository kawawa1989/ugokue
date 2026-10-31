"""顔の向きと表情の差分の、生成した絵のまとめ（頭だけを戻した絵、頭以外の変化、向きの判定）。"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image

from .head import outside_difference, paste_head
from .pose import estimate_pose, judge
from .view_spec import FRONT, View, ViewSpec, load_spec, parse_spec, view_name

__all__ = ["FRONT", "View", "ViewSpec", "contact_sheet", "load_spec", "parse_spec", "view_name", "write_views"]


def write_views(
    out_dir: str | Path,
    source: np.ndarray,
    raw: dict[str, np.ndarray],
    mask: np.ndarray,
    landmarks: dict[str, list[dict[str, Any]]],
    run: dict[str, Any],
    angles: dict[str, tuple[int, int]],
) -> dict[str, Any]:
    """生成した絵と、頭だけを戻した絵を書き出し、頭以外の変化と向きの判定を views.json にまとめる。"""
    out_dir = Path(out_dir)
    (out_dir / "raw").mkdir(parents=True, exist_ok=True)
    (out_dir / "views").mkdir(parents=True, exist_ok=True)
    Image.fromarray(source[..., :3]).save(out_dir / "views" / f"{FRONT}.png")
    Image.fromarray((mask * 255).round().astype(np.uint8), "L").save(out_dir / "mask.png")
    front = estimate_pose(landmarks.get(FRONT, []))
    entries: dict[str, Any] = {}
    for name, image in raw.items():
        Image.fromarray(image[..., :3]).save(out_dir / "raw" / f"{name}.png")
        Image.fromarray(paste_head(source, image, mask)).save(out_dir / "views" / f"{name}.png")
        angle_x, angle_y = angles.get(name, (0, 0))
        entries[name] = {
            "outsideHead": {k: round(v, 5) for k, v in outside_difference(source, image, mask).items()},
            "pose": judge(front, estimate_pose(landmarks.get(name, [])), angle_x, angle_y),
        }
    summary = {"run": run, "views": entries}
    (out_dir / "views.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (out_dir / "landmarks.json").write_text(json.dumps(landmarks) + "\n", encoding="utf-8")
    return summary


def contact_sheet(out_dir: str | Path, expressions: list[str], width: int = 240) -> None:
    """views/ の絵を、向きの 3×3 と表情の順に並べた sheet.png を書き出す（人の確認用）。"""
    out_dir = Path(out_dir)
    grid = [[view_name(x, y) for x in (-30, 0, 30)] for y in (20, 0, -20)]
    rows = grid + [expressions]
    first = Image.open(out_dir / "views" / f"{FRONT}.png")
    height = round(first.height * width / first.width)
    sheet = Image.new("RGB", (width * 3, height * len(rows)), (255, 255, 255))
    for r, row in enumerate(rows):
        for c, name in enumerate(row):
            path = out_dir / "views" / f"{name}.png"
            if path.exists():
                sheet.paste(Image.open(path).convert("RGB").resize((width, height)), (c * width, r * height))
    sheet.save(out_dir / "sheet.png")
