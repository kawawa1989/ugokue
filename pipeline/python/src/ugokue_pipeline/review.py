"""顔の向きのキーフォームを、生成した ±45° の参考の絵と見比べてエージェントが合否を判定するための資料（並べた画像と数値）を作り、判定の結果（review.json）を確かめる。"""

from __future__ import annotations

import io
import json
from pathlib import Path
from typing import Any

import cv2
import numpy as np
from PIL import Image, ImageDraw

from ugokue_format.model import Mesh, Model
from ugokue_format.package import ModelPackage, load

from . import render
from .deform import deformed_positions
from .head3d import ANGLES_Y, HeadParams
from .pose import MIN_DELTA, estimate_pose
from .view_spec import FRONT, view_name

# 判定する向き（左右は最大の角度だけ、上下は格子のすべて）。
REVIEW_X = (-45, 45)
REVIEW_Y = tuple(ANGLES_Y)
# 三角形の頂点と中心のテクスチャの不透明度の平均がこれ以上なら、描かれている三角形とみなす。
OPAQUE_ALPHA = 0.5
FEATURE_ROLES = ("eye_white", "iris", "eyelid", "eyebrow", "mouth", "nose")
EYE_ROLES = ("eye_white", "iris", "eyelid", "eyebrow")
# 並べる画像の切り出し（頭の半径に対する割合）。頭は中心から左右・上・下、目と口元は特徴の外枠からの余白。
HEAD_CROP = (1.6, 1.4, 1.6)
DETAIL_MARGIN = 0.25
CELL_HEIGHT = 300
MAX_ITERATIONS = 3
VERDICTS = ("pass", "retry", "stop")
CRITERIA = ("direction", "breakage")
REVIEW_DIR = "review"
RENDER_LANDMARKS = "render-landmarks.json"


def opaque_triangles(mesh: Mesh, texture: np.ndarray) -> np.ndarray:
    """メッシュの三角形のうち、テクスチャが描かれている（不透明な）ものの印。texture は RGBA の画像。"""
    alpha = texture[..., 3].astype(np.float64) / 255
    height, width = alpha.shape
    uvs = np.asarray(mesh.uvs, dtype=np.float64)
    triangles = np.asarray(mesh.triangles, dtype=np.int64).reshape(-1, 3)
    corners = uvs[triangles]
    samples = np.concatenate([corners, corners.mean(axis=1, keepdims=True)], axis=1)
    xs = np.clip((samples[..., 0] * width).astype(np.int64), 0, width - 1)
    ys = np.clip((samples[..., 1] * height).astype(np.int64), 0, height - 1)
    return alpha[ys, xs].mean(axis=1) >= OPAQUE_ALPHA


def _texture(package: ModelPackage, mesh: Mesh) -> np.ndarray:
    return np.array(Image.open(io.BytesIO(package.textures[mesh.texture])).convert("RGBA"))


def _stretches(rest: np.ndarray, deformed: np.ndarray, triangles: np.ndarray) -> np.ndarray:
    """三角形ごとの伸び（特異値の大きいほうと小さいほう）。基準の形でつぶれた三角形は除く。"""
    before = np.stack([rest[triangles[:, 1]] - rest[triangles[:, 0]], rest[triangles[:, 2]] - rest[triangles[:, 0]]], axis=-1)
    after = np.stack([deformed[triangles[:, 1]] - deformed[triangles[:, 0]], deformed[triangles[:, 2]] - deformed[triangles[:, 0]]], axis=-1)
    keep = np.abs(np.linalg.det(before)) > 1e-9
    transform = after[keep] @ np.linalg.inv(before[keep])
    return np.linalg.svd(transform, compute_uv=False)


def angle_metrics(package: ModelPackage, meshes: list[str], angle_x: float, angle_y: float) -> dict[str, Any]:
    """1つの向きでの、描かれている三角形の伸びの分布と、顔の外に出た目・眉・口・鼻の頂点の数。"""
    model = package.model
    by_id = {m.id: m for m in model.meshes}
    values = {"AngleX": angle_x, "AngleY": angle_y}
    roles = {p.mesh: p.role for p in model.parts if p.mesh}
    singular, face_mask, features = [], np.zeros((model.canvas.height, model.canvas.width), np.uint8), {}
    positions = {}
    for mesh_id in meshes:
        mesh = by_id[mesh_id]
        triangles = np.asarray(mesh.triangles, dtype=np.int64).reshape(-1, 3)
        opaque = opaque_triangles(mesh, _texture(package, mesh))
        deformed = deformed_positions(mesh, [g for g in model.deformation.keyform_groups if g.mesh == mesh_id], values)
        positions[mesh_id] = (deformed, triangles[opaque])
        if opaque.any():
            singular.append(_stretches(np.asarray(mesh.vertices, dtype=np.float64), deformed, triangles[opaque]))
        if roles.get(mesh_id) == "face":
            polygons = [np.round(deformed[t]).astype(np.int32) for t in triangles[opaque]]
            cv2.fillPoly(face_mask, polygons, 1)
    if face_mask.any():
        for mesh_id, (deformed, drawn) in positions.items():
            if roles.get(mesh_id) not in FEATURE_ROLES or len(drawn) == 0:
                continue
            points = np.round(deformed[np.unique(drawn)]).astype(np.int64)
            xs = np.clip(points[:, 0], 0, face_mask.shape[1] - 1)
            ys = np.clip(points[:, 1], 0, face_mask.shape[0] - 1)
            outside = int((face_mask[ys, xs] == 0).sum())
            if outside:
                features[mesh_id] = outside
    values_all = np.concatenate(singular) if singular else np.ones((1, 2))
    return {
        "stretch": {
            "triangles": int(len(values_all)),
            "maxP50": round(float(np.percentile(values_all[:, 0], 50)), 3),
            "maxP95": round(float(np.percentile(values_all[:, 0], 95)), 3),
            "maxP99": round(float(np.percentile(values_all[:, 0], 99)), 3),
            "minP1": round(float(np.percentile(values_all[:, 1], 1)), 3),
            "minP5": round(float(np.percentile(values_all[:, 1], 5)), 3),
        },
        "featureVerticesOutsideFace": features,
    }


def _drawn_positions(package: ModelPackage, part_id: str, values: dict[str, float]) -> np.ndarray:
    """パーツの、描かれている三角形の頂点の変形後の位置。描かれている三角形がなければ全頂点。"""
    model = package.model
    part = next(p for p in model.parts if p.id == part_id)
    mesh = next(m for m in model.meshes if m.id == part.mesh)
    deformed = deformed_positions(mesh, [g for g in model.deformation.keyform_groups if g.mesh == mesh.id], values)
    drawn = np.unique(np.asarray(mesh.triangles, dtype=np.int64).reshape(-1, 3)[opaque_triangles(mesh, _texture(package, mesh))])
    return deformed[drawn] if len(drawn) else deformed


def _face_mask(package: ModelPackage, values: dict[str, float]) -> np.ndarray:
    """顔のパーツの描かれている三角形を、変形後の位置で塗った画像。"""
    model = package.model
    mask = np.zeros((model.canvas.height, model.canvas.width), np.uint8)
    for part in model.parts:
        if part.role == "face" and part.mesh:
            mesh = next(m for m in model.meshes if m.id == part.mesh)
            deformed = deformed_positions(mesh, [g for g in model.deformation.keyform_groups if g.mesh == mesh.id], values)
            triangles = np.asarray(mesh.triangles, dtype=np.int64).reshape(-1, 3)[opaque_triangles(mesh, _texture(package, mesh))]
            cv2.fillPoly(mask, [np.round(deformed[t]).astype(np.int32) for t in triangles], 1)
    return mask


def _contour_x(mask: np.ndarray, row: int, far_right: bool) -> float | None:
    """顔の範囲の、その行の奥側（far_right なら右）の端の x。顔がなければ None。"""
    xs = np.flatnonzero(mask[int(np.clip(row, 0, mask.shape[0] - 1))])
    if len(xs) == 0:
        return None
    return float(xs[-1] if far_right else xs[0])


def _side_shape(package: ModelPackage, values: dict[str, float], far_eye: str, far_right: bool, mouth_y: float, radius: float) -> dict[str, float] | None:
    """奥側の目尻から輪郭までの余白（正面の目の幅に対する割合は呼び出し側で求める）と、目の高さから口の高さまでの奥側の頬の輪郭のふくらみ（頭の半径に対する割合）。"""
    mask = _face_mask(package, values)
    eye = _drawn_positions(package, far_eye, values)
    corner = eye[np.argmax(eye[:, 0])] if far_right else eye[np.argmin(eye[:, 0])]
    edge = _contour_x(mask, round(corner[1]), far_right)
    top, bottom = round(eye[:, 1].mean()), round(mouth_y)
    rows = [(y, _contour_x(mask, y, far_right)) for y in range(top, bottom + 1)]
    rows = [(y, x) for y, x in rows if x is not None]
    if edge is None or len(rows) < 3:
        return None
    (y0, x0), (y1, x1) = rows[0], rows[-1]
    sign = 1 if far_right else -1
    bulge = max(sign * (x - (x0 + (x1 - x0) * (y - y0) / (y1 - y0))) for y, x in rows)
    return {"gap": sign * (edge - corner[0]), "bulge": bulge / radius}


def proportions(package: ModelPackage, angle_x: float, angle_y: float, radius: float) -> dict[str, Any] | None:
    """目の釣り合いと奥側の輪郭の形を、正面と比べる。手前と奥の白目の幅（正面に対する倍率）と、その比、奥の目尻から輪郭までの余白（正面の白目の幅に対する割合）、
    奥側の頬のふくらみ（目の高さから口の高さまでの輪郭が、両端を結ぶ線からどれだけ外に出るか。頭の半径に対する割合）。白目か口か顔がなければ None。"""
    model = package.model
    roles = {p.id: p.role for p in model.parts if p.mesh}
    eyes = sorted(pid for pid, role in roles.items() if role == "eye_white")
    mouths = [pid for pid, role in roles.items() if role == "mouth"]
    if len(eyes) != 2 or not mouths or angle_x == 0:
        return None
    front_values = {"AngleX": 0.0, "AngleY": 0.0}
    values = {"AngleX": angle_x, "AngleY": angle_y}
    front = {pid: _drawn_positions(package, pid, front_values) for pid in eyes}
    turned = {pid: _drawn_positions(package, pid, values) for pid in eyes}
    far_right = angle_x > 0
    far = max(eyes, key=lambda pid: front[pid][:, 0].mean() * (1 if far_right else -1))
    near = next(pid for pid in eyes if pid != far)
    width = {pid: np.ptp(front[pid][:, 0]) for pid in eyes}
    near_scale = float(np.ptp(turned[near][:, 0]) / width[near])
    far_scale = float(np.ptp(turned[far][:, 0]) / width[far])
    mouth_front = float(_drawn_positions(package, mouths[0], front_values)[:, 1].mean())
    mouth_turned = float(_drawn_positions(package, mouths[0], values)[:, 1].mean())
    side_front = _side_shape(package, front_values, far, far_right, mouth_front, radius)
    side_turned = _side_shape(package, values, far, far_right, mouth_turned, radius)
    if side_front is None or side_turned is None:
        return None
    eye_width = float(np.mean(list(width.values())))
    return {
        "nearEye": near,
        "farEye": far,
        "nearEyeWidth": round(near_scale, 3),
        "farEyeWidth": round(far_scale, 3),
        "farToNear": round(far_scale / near_scale, 3),
        "farEyeGap": {"front": round(side_front["gap"] / eye_width, 3), "turned": round(side_turned["gap"] / eye_width, 3)},
        "cheekBulge": {"front": round(side_front["bulge"], 3), "turned": round(side_turned["bulge"], 3)},
    }


def pose_comparison(front: list[dict] | None, view: list[dict] | None, reference_front: list[dict] | None, reference: list[dict] | None) -> dict[str, Any]:
    """正面からの向きの値の変化を、描いたモデルと参考の絵で比べる。ratio は モデルの変化 ÷ 参考の絵の変化（1 に近いほど同じ量、負なら逆向き）。"""
    result: dict[str, Any] = {}
    for key, (a, b) in {"render": (front, view), "reference": (reference_front, reference)}.items():
        start, end = estimate_pose(a or []), estimate_pose(b or [])
        result[key] = None if start is None or end is None else {"yawDelta": round(end.yaw - start.yaw, 4), "pitchDelta": round(end.pitch - start.pitch, 4)}
    for axis in ("yaw", "pitch"):
        ratio = None
        if result["render"] and result["reference"] and abs(result["reference"][f"{axis}Delta"]) >= MIN_DELTA:
            ratio = round(result["render"][f"{axis}Delta"] / result["reference"][f"{axis}Delta"], 3)
        result[f"{axis}Ratio"] = ratio
    return result


def review_pack(keyform_dir: Path, reference_dir: Path | None) -> dict[str, Any]:
    """keyform_dir（keyforms の出力）の中の review/ に、判定する向きごとの並べた画像と metrics.json を書いて、数値を返す。
    参考の絵は reference_dir/views/、その特徴点は reference_dir/landmarks.json、描いたモデルの特徴点は review/render-landmarks.json から読む（あれば）。"""
    model_dir = keyform_dir / "model"
    package = load(model_dir)
    evaluation = json.loads((keyform_dir / "evaluation.json").read_text(encoding="utf-8"))
    out = keyform_dir / REVIEW_DIR
    out.mkdir(parents=True, exist_ok=True)
    render_images(keyform_dir)
    meshes = sorted({g.mesh for g in package.model.deformation.keyform_groups if g.parameters == ["AngleX", "AngleY"]})
    render_faces = _read_json(out / RENDER_LANDMARKS) or {}
    reference_faces = _read_json(reference_dir / "landmarks.json") if reference_dir else None
    reference_faces = reference_faces or {}
    angles = []
    for y in REVIEW_Y:
        for x in REVIEW_X:
            name = view_name(x, y)
            entry = {"angleX": x, "angleY": y, **angle_metrics(package, meshes, x, y)}
            entry["proportions"] = proportions(package, x, y, evaluation["head"]["radius"])
            entry["pose"] = pose_comparison(render_faces.get(FRONT), render_faces.get(name), reference_faces.get(FRONT), reference_faces.get(name))
            entry["reference"] = bool(reference_dir and (reference_dir / "views" / f"{name}.png").is_file())
            angles.append(entry)
    metrics = {"params": evaluation.get("params", HeadParams().to_json()), "head": evaluation["head"], "angles": angles}
    (out / "metrics.json").write_text(json.dumps(metrics, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for y in REVIEW_Y:
        _sheet(out / f"sheet-y{y:+03d}.png", package, keyform_dir / "renders", reference_dir, evaluation["head"], y)
    return metrics


def render_images(keyform_dir: Path) -> dict[str, bytes]:
    """並べる画像と特徴点の検出に使う、描いたモデルの PNG（上下の向きごとの正面と、判定する向き）。なければ描く。"""
    renders = keyform_dir / "renders"
    renders.mkdir(parents=True, exist_ok=True)
    images = {}
    for x, y in [(x, y) for y in REVIEW_Y for x in (0, *REVIEW_X)]:
        path = renders / f"{view_name(x, y)}.png"
        if not path.is_file():
            render.render(keyform_dir / "model", path, {"AngleX": x, "AngleY": y})
        images[view_name(x, y)] = path.read_bytes()
    return images


def _read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8")) if path.is_file() else None


def _sheet(path: Path, package: ModelPackage, renders: Path, reference_dir: Path | None, head: dict[str, Any], angle_y: int) -> None:
    """1つの上下の向きについて、左右の最大の角度ごとに1行、正面・モデル・参考の絵・目元（モデル、参考）・口元とあご（モデル、参考）を並べる。"""
    model = package.model
    cx, cy = head["center"]
    radius = head["radius"]
    side, up, down = HEAD_CROP
    head_box = _clip((cx - side * radius, cy - up * radius, cx + side * radius, cy + down * radius), model)
    front = _image(renders / f"{view_name(0, angle_y)}.png")
    rows = []
    for x in REVIEW_X:
        name = view_name(x, angle_y)
        rendered = _image(renders / f"{name}.png")
        reference_path = reference_dir / "views" / f"{name}.png" if reference_dir else None
        reference = _image(reference_path) if reference_path and reference_path.is_file() else None
        eyes = _feature_box(package, EYE_ROLES, x, angle_y, radius)
        chin = _feature_box(package, ("mouth", "face"), x, angle_y, radius, chin=True)
        cells = [("front", front, head_box), (f"model {name}", rendered, head_box), ("reference", reference, head_box)]
        cells += [("model eyes", rendered, eyes), ("reference eyes", reference, eyes), ("model chin", rendered, chin), ("reference chin", reference, chin)]
        rows.append(cells)
    widths = [round(CELL_HEIGHT * (box[2] - box[0]) / (box[3] - box[1])) for _, _, box in rows[0]]
    sheet = Image.new("RGB", (sum(widths), CELL_HEIGHT * len(rows)), (255, 255, 255))
    draw = ImageDraw.Draw(sheet)
    for r, cells in enumerate(rows):
        left = 0
        for (label, image, box), width in zip(cells, widths):
            if image is not None:
                sheet.paste(image.crop(box).resize((width, CELL_HEIGHT)), (left, r * CELL_HEIGHT))
            else:
                draw.rectangle((left, r * CELL_HEIGHT, left + width - 1, (r + 1) * CELL_HEIGHT - 1), fill=(230, 230, 230))
            draw.rectangle((left, r * CELL_HEIGHT, left + width - 1, (r + 1) * CELL_HEIGHT - 1), outline=(160, 160, 160))
            draw.rectangle(draw.textbbox((left + 4, r * CELL_HEIGHT + 4), label), fill=(255, 255, 255))
            draw.text((left + 4, r * CELL_HEIGHT + 4), label, fill=(200, 0, 0))
            left += width
    sheet.save(path)


def _image(path: Path) -> Image.Image:
    return Image.open(path).convert("RGB")


def _clip(box: tuple[float, float, float, float], model: Model) -> tuple[int, int, int, int]:
    x1, y1, x2, y2 = box
    return int(max(0, x1)), int(max(0, y1)), int(min(model.canvas.width, x2)), int(min(model.canvas.height, y2))


def _feature_box(package: ModelPackage, roles: tuple[str, ...], angle_x: float, angle_y: float, radius: float, chin: bool = False) -> tuple[int, int, int, int]:
    """その向きで変形したパーツの、描かれている頂点（細い線だけのパーツはすべての頂点）の外枠に余白を足した正方形の範囲。chin なら、顔は下端（あご先）の点だけを使う。"""
    model = package.model
    meshes = {m.id: m for m in model.meshes}
    points = []
    for part in model.parts:
        if part.mesh and part.role in roles:
            mesh = meshes[part.mesh]
            groups = [g for g in model.deformation.keyform_groups if g.mesh == mesh.id]
            deformed = deformed_positions(mesh, groups, {"AngleX": angle_x, "AngleY": angle_y})
            drawn = np.unique(np.asarray(mesh.triangles, dtype=np.int64).reshape(-1, 3)[opaque_triangles(mesh, _texture(package, mesh))])
            if len(drawn):
                deformed = deformed[drawn]
            if chin and part.role == "face":
                deformed = deformed[[int(np.argmax(deformed[:, 1]))]]
            points.append(deformed)
    if not points:
        return _clip((0, 0, model.canvas.width, model.canvas.height), model)
    stacked = np.concatenate(points)
    (x1, y1), (x2, y2) = stacked.min(axis=0), stacked.max(axis=0)
    half = max(x2 - x1, y2 - y1) / 2 + DETAIL_MARGIN * radius
    cx, cy = (x1 + x2) / 2, (y1 + y2) / 2
    return _clip((cx - half, cy - half, cx + half, cy + half), model)


def validate_review(data: Any) -> list[str]:
    """エージェントが書いた判定の結果（review.json）の問題の一覧。問題がなければ空。"""
    if not isinstance(data, dict):
        return ["review must be an object"]
    issues = []
    for key in ("illustration", "summary"):
        if not isinstance(data.get(key), str) or not data[key].strip():
            issues.append(f"{key} must be a non-empty string")
    iteration = data.get("iteration")
    if isinstance(iteration, bool) or not isinstance(iteration, int) or not 1 <= iteration <= MAX_ITERATIONS:
        issues.append(f"iteration must be an integer from 1 to {MAX_ITERATIONS}")
    issues += _params_issues("params", data.get("params"))
    expected = {(x, y) for x in REVIEW_X for y in REVIEW_Y}
    seen = set()
    all_pass = True
    for index, entry in enumerate(data.get("angles") if isinstance(data.get("angles"), list) else []):
        key = (entry.get("angleX"), entry.get("angleY")) if isinstance(entry, dict) else None
        if key not in expected or key in seen:
            issues.append(f"angles[{index}] must be one of the review angles, once each")
            continue
        seen.add(key)
        for criterion in CRITERIA:
            judged = entry.get(criterion)
            if not isinstance(judged, dict) or not isinstance(judged.get("pass"), bool) or not isinstance(judged.get("reason"), str) or not judged["reason"].strip():
                issues.append(f"angles[{index}].{criterion} must have a boolean pass and a non-empty reason")
                all_pass = False
            else:
                all_pass = all_pass and judged["pass"]
    if expected - seen:
        issues.append("angles must cover " + ", ".join(view_name(x, y) for x, y in sorted(expected - seen)))
    verdict = data.get("verdict")
    changes = data.get("changes")
    if verdict not in VERDICTS:
        issues.append(f"verdict must be one of {', '.join(VERDICTS)}")
    change_issues = _params_issues("changes", changes)
    if change_issues:
        return issues + change_issues
    if verdict == "pass" and not all_pass:
        issues.append("verdict pass needs every criterion to pass")
    if verdict in ("pass", "stop") and changes:
        issues.append(f"verdict {verdict} must not have changes")
    if verdict == "retry":
        if all_pass and seen == expected:
            issues.append("verdict retry needs a failed criterion")
        if not changes:
            issues.append("verdict retry needs changes")
        if isinstance(iteration, int) and iteration >= MAX_ITERATIONS:
            issues.append(f"iteration {MAX_ITERATIONS} must end with pass or stop")
    return issues


def _params_issues(key: str, value: Any) -> list[str]:
    if not isinstance(value, dict):
        return [f"{key} must be an object"]
    try:
        HeadParams.from_json(value)
    except ValueError as e:
        return [f"{key}: {e}"]
    return []
