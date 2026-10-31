"""分解結果（PoC-08）と、向きを変えた絵（PoC-09）から、顔の向きのキーフォームを持つモデルを作り、評価する。"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import cv2
import numpy as np
from PIL import Image

from ugokue_format.package import save
from ugokue_format.validate import validate_dir

from . import render
from .convert import ordered_layers, to_model
from .estimate import ViewInput, apply_estimate, estimate_keyforms, head_parts, owner_map
from .evaluate import image_difference, landmark_error, mesh_quality
from .head import head_mask
from .result import load_result
from .view_spec import FRONT, load_spec

# 大きさと傾きの補正に使う特徴点（目・鼻・口）。輪郭（afd_00〜afd_04）は、顔の向きや手で検出が外れやすいので使わない。
STABLE_LANDMARKS = {f"afd_{i:02d}" for i in range(11, 28)}
# 左右の回り方の強さ。左右に回すときだけ、顔の前面の奥行きを幅の半分（固定テストモデルと同じ）より深くし、首の付け根のように頭の中心より奥の軸で回す。
# 生成した横向きの絵は ±30° より横顔に近いので、テストモデルと同じ値では、顔が髪の中で横に滑るだけに見える。
YAW_DEPTH_SCALE = 1.0
PIVOT_DEPTH = 0.5
QUALITY_X = list(range(-30, 31, 5))
QUALITY_Y = list(range(-20, 21, 5))
# 画像の差を測る範囲は、正面の頭の範囲（あごの少し下まで）を、イラストの短い辺のこの割合だけ広げたもの。
REGION_MARGIN = 0.04


def detected_landmarks(faces: list[dict[str, Any]]) -> dict[str, tuple[float, float]]:
    """anime-face-detector の検出結果から、いちばん確からしい顔の28点を afd_00〜afd_27 の ID で返す。"""
    if not faces:
        return {}
    face = max(faces, key=lambda f: f["bbox"][4])
    return {f"afd_{i:02d}": (float(x), float(y)) for i, (x, y, *_) in enumerate(face["keypoints"])}


def build(result_dir: Path, views_dir: Path, spec_path: Path, out_dir: Path) -> dict[str, Any]:
    """1枚のイラストについて、モデルを作って out_dir/model に書き、各向きで描いて評価した結果を返す。"""
    result = load_result(result_dir)
    package = to_model(result)
    landmarks = json.loads((views_dir / "landmarks.json").read_text(encoding="utf-8"))
    front_landmarks = detected_landmarks(landmarks[FRONT])
    inputs = []
    for view in load_spec(spec_path).views:
        if view.expression is not None:
            continue
        image = np.array(Image.open(views_dir / "views" / f"{view.name}.png").convert("RGB"))
        inputs.append(ViewInput(view.angle_x, view.angle_y, image, detected_landmarks(landmarks.get(view.name, []))))
    part_depths = {part_id: layer.depth_median for (part_id, _, _), layer in ordered_layers(result)}
    estimate = estimate_keyforms(
        package,
        result.source,
        front_landmarks,
        inputs,
        stable_landmarks=STABLE_LANDMARKS,
        part_depths=part_depths,
        yaw_depth_scale=YAW_DEPTH_SCALE,
        pivot_depth=PIVOT_DEPTH,
    )
    apply_estimate(package, estimate)
    model_dir = out_dir / "model"
    save(package, model_dir)
    issues = [str(x) for x in validate_dir(model_dir)]

    owner, ids = owner_map(package)
    heads = head_parts(package.model)
    meshes_of_parts = {p.id: p.mesh for p in package.model.parts}
    mesh_of_landmark = {}
    for landmark_id, (x, y) in front_landmarks.items():
        index = owner[int(np.clip(round(y), 0, owner.shape[0] - 1)), int(np.clip(round(x), 0, owner.shape[1] - 1))]
        if index >= 0 and ids[index] in heads:
            mesh_of_landmark[landmark_id] = meshes_of_parts[ids[index]]
    region = head_mask(result, margin=round(REGION_MARGIN * min(owner.shape)), feather=0) > 0

    (out_dir / "renders").mkdir(parents=True, exist_ok=True)
    (out_dir / "targets").mkdir(parents=True, exist_ok=True)
    front_render = render.render(model_dir, out_dir / "renders" / f"{FRONT}.png", {})
    views: dict[str, Any] = {}
    for view in inputs:
        name = f"x{view.angle_x:+03d}_y{view.angle_y:+03d}"
        values = {"AngleX": view.angle_x, "AngleY": view.angle_y}
        correspondence = estimate.correspondences[(view.angle_x, view.angle_y)]
        height, width = view.image.shape[:2]
        target = cv2.warpAffine(view.image, correspondence.normalize, (width, height), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)
        Image.fromarray(target).save(out_dir / "targets" / f"{name}.png")
        rendered = render.render(model_dir, out_dir / "renders" / f"{name}.png", values)
        views[name] = {
            "landmarks": landmark_error(package.model, mesh_of_landmark, front_landmarks, correspondence.normalized_landmarks, values),
            "imageBefore": image_difference(front_render, target, region),
            "imageAfter": image_difference(rendered, target, region),
            "normalize": {"scale": round(float(np.sqrt(abs(np.linalg.det(correspondence.normalize[:, :2])))), 3),
                          "rotationDegrees": round(float(np.degrees(np.arctan2(correspondence.normalize[1, 0], correspondence.normalize[0, 0]))), 2)},
        }
    quality = mesh_quality(package.model, [g.mesh for g in estimate.groups], QUALITY_X, QUALITY_Y)
    summary = {"validationIssues": issues, "views": views, "quality": quality, "vertexMatches": len(estimate.vertex_matches)}
    (out_dir / "evaluation.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    _sheet(out_dir, [f"x{v.angle_x:+03d}_y{v.angle_y:+03d}" for v in inputs])
    return summary


def _sheet(out_dir: Path, names: list[str], width: int = 200) -> None:
    """向きの 3×3 の位置に、生成した絵（大きさと傾きをそろえたもの）と、変形したモデルを描いた絵を左右に並べる（人の確認用）。"""
    cells = {FRONT: (out_dir / "renders" / f"{FRONT}.png", out_dir / "renders" / f"{FRONT}.png")}
    for name in names:
        cells[name] = (out_dir / "targets" / f"{name}.png", out_dir / "renders" / f"{name}.png")
    first = Image.open(cells[FRONT][0])
    height = round(first.height * width / first.width)
    sheet = Image.new("RGB", (width * 6, height * 3), (255, 255, 255))
    for row, y in enumerate((20, 0, -20)):
        for col, x in enumerate((-30, 0, 30)):
            name = f"x{x:+03d}_y{y:+03d}"
            if name not in cells:
                continue
            for k, path in enumerate(cells[name]):
                sheet.paste(Image.open(path).convert("RGB").resize((width, height)), ((2 * col + k) * width, row * height))
    sheet.save(out_dir / "sheet.png")
