"""分解結果（PoC-08）と正面の特徴点から、頭の3D形状を回して顔の向きのキーフォームを持つモデルを作り、評価する。向きを変えた絵（PoC-09）は確認用に並べるだけ。"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image

from ugokue_format.package import save
from ugokue_format.validate import validate_dir

from . import render
from .convert import to_model
from .evaluate import mesh_quality
from .head3d import ANGLES_X, ANGLES_Y, apply_keyforms, build_keyforms, fit_head
from .result import load_result
from .view_spec import FRONT

QUALITY_X = list(range(ANGLES_X[0], ANGLES_X[-1] + 1, 5))
QUALITY_Y = list(range(ANGLES_Y[0], ANGLES_Y[-1] + 1, 5))
# 比較シートで切り出す範囲（頭の半径に対する、頭の中心からの距離。左右、上、下）。
SHEET_CROP = (1.9, 1.5, 2.1)
SHEET_CELL_WIDTH = 220


def detected_landmarks(faces: list[dict[str, Any]]) -> dict[str, tuple[float, float]]:
    """anime-face-detector の検出結果から、いちばん確からしい顔の28点を afd_00〜afd_27 の ID で返す。"""
    if not faces:
        return {}
    face = max(faces, key=lambda f: f["bbox"][4])
    return {f"afd_{i:02d}": (float(x), float(y)) for i, (x, y, *_) in enumerate(face["keypoints"])}


def build(result_dir: Path, views_dir: Path, out_dir: Path) -> dict[str, Any]:
    """1枚のイラストについて、モデルを作って out_dir/model に書き、格子の各向きで描いた比較シートと評価を書いて、評価を返す。"""
    result = load_result(result_dir)
    package = to_model(result)
    landmarks = json.loads((views_dir / "landmarks.json").read_text(encoding="utf-8"))
    shape = fit_head(package, detected_landmarks(landmarks[FRONT]))
    groups = build_keyforms(package, shape)
    apply_keyforms(package, groups)
    model_dir = out_dir / "model"
    save(package, model_dir)
    issues = [str(x) for x in validate_dir(model_dir)]

    (out_dir / "renders").mkdir(parents=True, exist_ok=True)
    renders = {}
    for y in ANGLES_Y:
        for x in ANGLES_X:
            name = f"x{x:+03d}_y{y:+03d}"
            renders[name] = render.render(model_dir, out_dir / "renders" / f"{name}.png", {"AngleX": x, "AngleY": y})
    quality = mesh_quality(package.model, [g.mesh for g in groups], QUALITY_X, QUALITY_Y)
    summary = {
        "validationIssues": issues,
        "head": {"center": [round(float(v), 1) for v in shape.center], "radius": round(shape.radius, 1), "rows": len(shape.centers)},
        "quality": quality,
    }
    (out_dir / "evaluation.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    _sheet(out_dir / "sheet.png", renders, views_dir / "views", shape.center, shape.radius)
    return summary


def _sheet(path: Path, renders: dict[str, np.ndarray], views: Path, center: np.ndarray, radius: float) -> None:
    """上向き・正面・下向きの行ごとに、格子の各向きで描いたモデルを並べ、その下に、生成した向きの絵があれば同じ列に並べる（人の確認用）。"""
    side, up, down = SHEET_CROP
    first = next(iter(renders.values()))
    box = (
        int(max(0, center[0] - side * radius)),
        int(max(0, center[1] - up * radius)),
        int(min(first.shape[1], center[0] + side * radius)),
        int(min(first.shape[0], center[1] + down * radius)),
    )
    width = SHEET_CELL_WIDTH
    height = round(width * (box[3] - box[1]) / (box[2] - box[0]))
    sheet = Image.new("RGB", (width * len(ANGLES_X), height * 2 * len(ANGLES_Y)), (255, 255, 255))
    for row, y in enumerate(sorted(ANGLES_Y, reverse=True)):
        for col, x in enumerate(ANGLES_X):
            name = f"x{x:+03d}_y{y:+03d}"
            sheet.paste(Image.fromarray(renders[name]).crop(box).resize((width, height)), (col * width, 2 * row * height))
            generated = views / f"{name}.png"
            if generated.is_file():
                image = Image.open(generated).convert("RGB")
                if image.size == (first.shape[1], first.shape[0]):
                    sheet.paste(image.crop(box).resize((width, height)), (col * width, (2 * row + 1) * height))
    sheet.save(path)
