"""レビュー役が判定の結果（review.json）に描く修正の線（モデルの絵のキャンバスの画素座標の折れ線）を、確かめ、モデルの絵に重ねて描き、今の顔の輪郭とのずれを測る。"""

from __future__ import annotations

import json
import math
from pathlib import Path
from typing import Any

import cv2
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter1d

from ugokue_format.package import ModelPackage, load

from .view_spec import view_name

# 修正の線の種類。contour は顔の輪郭の目標の線で、今の輪郭とのずれを測る。other は目や髪など、それ以外の修正。
SKETCH_KINDS = ("contour", "other")
# 座標を読むための格子の間隔（キャンバスの画素）と、格子の絵・重ねた絵の高さ（画素）。
GRID_STEP = 50
GRID_HEIGHT = 900
# 頭の切り出し（頭の半径に対する、頭の中心からの距離。左右、上、下）。
SKETCH_CROP = (1.4, 0.6, 1.7)
# ずれを測るときに、線の上に点を置く間隔（キャンバスの画素）。
SAMPLE_STEP = 2.0
# 今の輪郭を、輪郭に沿ってならす幅（点の数）。
CONTOUR_SMOOTHING = 4
CURRENT_COLOR = (230, 40, 40)
SKETCH_COLOR = (0, 170, 60)


def crop_box(head: dict[str, Any], width: int, height: int) -> tuple[int, int, int, int]:
    """頭の中心と半径から、格子と修正の線を描く範囲を決める。"""
    cx, cy = head["center"]
    radius = head["radius"]
    side, up, down = SKETCH_CROP
    return int(max(0, cx - side * radius)), int(max(0, cy - up * radius)), int(min(width, cx + side * radius)), int(min(height, cy + down * radius))


def _scaled(image: Image.Image, box: tuple[int, int, int, int]) -> tuple[Image.Image, float]:
    scale = GRID_HEIGHT / (box[3] - box[1])
    cropped = image.convert("RGB").crop(box)
    return cropped.resize((round(cropped.width * scale), GRID_HEIGHT)), scale


def grid_image(image: Image.Image, box: tuple[int, int, int, int]) -> Image.Image:
    """切り出して拡大した絵に、キャンバスの座標の格子と目盛りの数字を重ねる（レビュー役が座標を読むため）。"""
    out, scale = _scaled(image, box)
    draw = ImageDraw.Draw(out, "RGBA")
    for x in range(math.ceil(box[0] / GRID_STEP) * GRID_STEP, box[2], GRID_STEP):
        px = (x - box[0]) * scale
        draw.line([(px, 0), (px, out.height)], fill=(0, 120, 255, 110 if x % (2 * GRID_STEP) == 0 else 55))
        draw.text((px + 2, 2), str(x), fill=(0, 80, 220, 255))
        draw.text((px + 2, out.height - 12), str(x), fill=(0, 80, 220, 255))
    for y in range(math.ceil(box[1] / GRID_STEP) * GRID_STEP, box[3], GRID_STEP):
        py = (y - box[1]) * scale
        draw.line([(0, py), (out.width, py)], fill=(0, 120, 255, 110 if y % (2 * GRID_STEP) == 0 else 55))
        draw.text((2, py + 2), str(y), fill=(0, 80, 220, 255))
        draw.text((out.width - 28, py + 2), str(y), fill=(0, 80, 220, 255))
    return out


def face_contour(mask: np.ndarray) -> np.ndarray:
    """顔の範囲（塗った画像）の、いちばん大きい外側の輪郭の点（キャンバスの画素、x と y）。顔がなければ空。
    範囲は三角形ごとに塗るので階段状になる。輪郭に沿ってならして、描いた線に近づける。"""
    contours, _ = cv2.findContours(mask.astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    if not contours:
        return np.zeros((0, 2))
    points = max(contours, key=len)[:, 0, :].astype(np.float64)
    return gaussian_filter1d(points, CONTOUR_SMOOTHING, axis=0, mode="wrap") if len(points) > 4 * CONTOUR_SMOOTHING else points


def densify(points: np.ndarray, step: float = SAMPLE_STEP) -> np.ndarray:
    """折れ線の上に、step 画素おきに点を置く。"""
    result = [points[0]]
    for a, b in zip(points[:-1], points[1:]):
        count = max(1, int(np.ceil(np.linalg.norm(b - a) / step)))
        result.extend(a + (b - a) * (i / count) for i in range(1, count + 1))
    return np.array(result)


def contour_distance(sketch: np.ndarray, contour: np.ndarray) -> dict[str, float] | None:
    """目標の線の各点から、今の輪郭のいちばん近い点までの距離の平均と最大（画素）。輪郭がなければ None。"""
    if len(contour) == 0:
        return None
    samples = densify(sketch)
    distances = np.sqrt(((samples[:, None, :] - contour[None, :, :]) ** 2).sum(axis=-1)).min(axis=1)
    return {"mean": round(float(distances.mean()), 1), "max": round(float(distances.max()), 1)}


def validate_sketches(sketches: Any, angles: set[tuple[int, int]]) -> list[str]:
    """判定の結果の sketches（なくてもよい）の問題の一覧。各要素は、判定する向き、種類、ラベル、2点以上の点（有限の数の [x, y]）、説明を持つ。"""
    if sketches is None:
        return []
    if not isinstance(sketches, list):
        return ["sketches must be a list"]
    issues = []
    for index, sketch in enumerate(sketches):
        where = f"sketches[{index}]"
        if not isinstance(sketch, dict):
            issues.append(f"{where} must be an object")
            continue
        if (sketch.get("angleX"), sketch.get("angleY")) not in angles:
            issues.append(f"{where} must be at one of the review angles")
        if sketch.get("kind") not in SKETCH_KINDS:
            issues.append(f"{where}.kind must be one of {', '.join(SKETCH_KINDS)}")
        for key in ("label", "note"):
            if not isinstance(sketch.get(key), str) or not sketch[key].strip():
                issues.append(f"{where}.{key} must be a non-empty string")
        points = sketch.get("points")
        valid = isinstance(points, list) and len(points) >= 2 and all(
            isinstance(p, list) and len(p) == 2 and all(isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v) for v in p) for p in points
        )
        if not valid:
            issues.append(f"{where}.points must be two or more [x, y] pairs of finite numbers")
    return issues


def draw_sketches(review_path: Path) -> dict[str, Any]:
    """review.json の修正の線を、その段階（stage）のモデルの絵に重ねて review/sketch-<段階>-<回数>-<向き>.png に描き（今の輪郭は赤、修正の線は緑）、
    輪郭の線と今の輪郭のずれを review/sketch-<段階>-<回数>.json に書いて返す。"""
    from .review import _face_mask, renders_for

    review = json.loads(review_path.read_text(encoding="utf-8"))
    keyform_dir = review_path.parent.parent
    package = load(keyform_dir / "model")
    head = json.loads((keyform_dir / "evaluation.json").read_text(encoding="utf-8"))["head"]
    canvas = package.model.canvas
    box = crop_box(head, canvas.width, canvas.height)
    iteration = review.get("iteration", 0)
    stage = review.get("stage", "full")
    renders = renders_for(keyform_dir, stage)
    by_angle: dict[tuple[int, int], list[dict[str, Any]]] = {}
    for sketch in review.get("sketches") or []:
        by_angle.setdefault((int(sketch["angleX"]), int(sketch["angleY"])), []).append(sketch)
    result = []
    for (x, y), sketches in sorted(by_angle.items()):
        name = view_name(x, y)
        contour = face_contour(_face_mask(package, {"AngleX": x, "AngleY": y}))
        image, scale = _scaled(Image.open(renders / f"{name}.png"), box)
        draw = ImageDraw.Draw(image)
        to_image = lambda p: [((px - box[0]) * scale, (py - box[1]) * scale) for px, py in p]  # noqa: E731
        if len(contour):
            draw.line(to_image(np.vstack([contour, contour[:1]])), fill=CURRENT_COLOR, width=2)
        for sketch in sketches:
            points = np.array(sketch["points"], dtype=np.float64)
            draw.line(to_image(points), fill=SKETCH_COLOR, width=4)
            lx, ly = to_image(points[:1])[0]
            draw.text((lx + 6, ly - 14), sketch["label"], fill=SKETCH_COLOR)
            entry = {"angleX": x, "angleY": y, "kind": sketch["kind"], "label": sketch["label"]}
            if sketch["kind"] == "contour":
                entry["distance"] = contour_distance(points, contour)
            result.append(entry)
        image.save(review_path.parent / f"sketch-{stage}-{iteration}-{name}.png")
    summary = {"review": review_path.name, "stage": stage, "sketches": result}
    (review_path.parent / f"sketch-{stage}-{iteration}.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return summary


def write_grids(renders: Path, out: Path, prefix: str, package: ModelPackage, head: dict[str, Any], names: list[str]) -> None:
    """renders にある、判定する向きと正面のモデルの絵に格子を重ねて、out/<prefix>-<向き>.png に書く。"""
    canvas = package.model.canvas
    box = crop_box(head, canvas.width, canvas.height)
    for name in names:
        grid_image(Image.open(renders / f"{name}.png"), box).save(out / f"{prefix}-{name}.png")
