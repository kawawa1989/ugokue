"""エージェントが顔の向きの合否を判定するための資料（並べた画像と数値）と、判定の結果の確認のテスト。"""

from __future__ import annotations

import copy
import io
import json
import os

import numpy as np
import pytest
from PIL import Image

from test_head3d import synthetic_face
from ugokue_format.model import Keyform, KeyformGroup
from ugokue_format.package import load, save
from ugokue_format.validate import validate_dir
from ugokue_pipeline import render
from ugokue_pipeline.deform import deformed_positions
from ugokue_pipeline.head3d import HeadParams, apply_keyforms, build_keyforms, fit_head
from ugokue_pipeline.keyforms import load_params
from ugokue_pipeline.sketch import draw_sketches
from ugokue_pipeline.view_spec import load_spec
from ugokue_pipeline.review import REVIEW_X, REVIEW_Y, _covered, _covering_masks, angle_metrics, face_only, opaque_triangles, pose_comparison, proportions, render_images, review_pack, validate_review


@pytest.fixture(scope="module")
def turned():
    package, landmarks = synthetic_face()
    shape = fit_head(package, landmarks)
    apply_keyforms(package, build_keyforms(package, shape))
    return package, shape


def _mesh_of_role(package, role):
    part = next(p for p in package.model.parts if p.role == role and p.mesh)
    return next(m for m in package.model.meshes if m.id == part.mesh)


def _angle_meshes(package):
    return sorted({g.mesh for g in package.model.deformation.keyform_groups})


def test_opaque_triangles_skip_the_transparent_corners_of_the_texture(turned) -> None:
    package, _ = turned
    face = _mesh_of_role(package, "face")
    texture = np.array(Image.open(io.BytesIO(package.textures[face.texture])).convert("RGBA"))

    opaque = opaque_triangles(face, texture)

    assert 0 < opaque.sum() < len(face.triangles)
    corner = int(np.argmin(np.asarray(face.vertices).sum(axis=1)))
    assert not any(opaque[i] for i, t in enumerate(face.triangles) if corner in t)


def test_angle_metrics_are_neutral_at_the_front_and_count_features_pushed_off_the_face(turned) -> None:
    package, _ = turned
    front = angle_metrics(package, _angle_meshes(package), 0, 0)
    assert front["stretch"]["maxP99"] == pytest.approx(1.0) and front["stretch"]["minP1"] == pytest.approx(1.0)
    assert front["featureVerticesOutsideFace"] == {}

    turned_45 = angle_metrics(package, _angle_meshes(package), 45, 0)
    assert turned_45["stretch"]["maxP99"] >= 1.0 and turned_45["stretch"]["triangles"] == front["stretch"]["triangles"]

    pushed = copy.deepcopy(package)
    eye = _mesh_of_role(pushed, "eye_white")
    pushed.model.deformation.keyform_groups.append(
        KeyformGroup("push", eye.id, ["AngleX"], [[0, 45]], "linear", [Keyform([0.0], [[0.0, 0.0]] * len(eye.vertices)), Keyform([45.0], [[-200.0, 0.0]] * len(eye.vertices))])
    )
    outside = angle_metrics(pushed, _angle_meshes(pushed), 45, 0)["featureVerticesOutsideFace"]
    assert outside.get(eye.id, 0) > 0



def test_stretch_skips_triangles_hidden_behind_parts_drawn_in_front(turned) -> None:
    package, _ = turned
    meshes = _angle_meshes(package)
    base = angle_metrics(package, meshes, 45, 0)["stretch"]
    # 髪を外すと、髪の下だったところが見えるので、隠れて数えない三角形が減る。
    assert base["hidden"] > angle_metrics(face_only(package), _angle_meshes(face_only(package)), 45, 0)["stretch"]["hidden"]

    # 手前のパーツに隠れる顔の三角形だけが使う頂点を、45° で横につぶしても、伸びの数値は変わらない（見えない所は数えない）。
    face = _mesh_of_role(package, "face")
    values = {"AngleX": 45, "AngleY": 0}
    triangles = np.asarray(face.triangles).reshape(-1, 3)
    drawn = triangles[opaque_triangles(face, np.array(Image.open(io.BytesIO(package.textures[face.texture])).convert("RGBA")))]
    deformed = deformed_positions(face, [g for g in package.model.deformation.keyform_groups if g.mesh == face.id], values)
    hidden = _covered(_covering_masks(package, values)[face.id], deformed, drawn)
    only = np.setdiff1d(np.unique(drawn[hidden]), np.unique(drawn[~hidden]))
    assert len(only) > 0
    offsets = np.zeros((len(face.vertices), 2))
    offsets[only, 0] = -0.9 * (deformed[only, 0] - deformed[only, 0].mean())
    squashed = copy.deepcopy(package)
    squashed.model.deformation.keyform_groups.append(
        KeyformGroup("squash", face.id, ["AngleX"], [[0, 45]], "linear", [Keyform([0.0], [[0.0, 0.0]] * len(face.vertices)), Keyform([45.0], offsets.tolist())])
    )
    after = angle_metrics(squashed, meshes, 45, 0)["stretch"]
    assert after["minP1"] == base["minP1"] and after["maxP99"] == base["maxP99"]


def test_face_stretch_counts_only_the_parts_kept_for_the_face_stage(turned) -> None:
    package, _ = turned
    metrics = angle_metrics(package, _angle_meshes(package), 45, 0)
    bare = face_only(package)
    kept = {m.id for m in bare.model.meshes}

    # 顔の段階の伸びは、髪などを外したモデルに残るパーツの三角形だけを数える（隠れて見えない所は、髪を付けたモデルで決める）。
    assert metrics["faceStretch"]["triangles"] == sum(
        int(opaque_triangles(m, np.array(Image.open(io.BytesIO(package.textures[m.texture])).convert("RGBA"))).sum()) for m in package.model.meshes if m.id in kept and m.id in _angle_meshes(package)
    )
    assert metrics["faceStretch"]["triangles"] < metrics["stretch"]["triangles"]

def test_proportions_compare_the_near_and_far_eye_and_the_far_cheek_with_the_front(turned) -> None:
    package, shape = turned

    right = proportions(package, 45, 0, shape.radius)
    left = proportions(package, -45, 0, shape.radius)

    assert proportions(package, 0, 0, shape.radius) is None
    assert (right["nearEye"], right["farEye"]) == ("eye_white_r", "eye_white_l")
    assert (left["nearEye"], left["farEye"]) == ("eye_white_l", "eye_white_r")
    assert right["farEyeWidth"] < 1 < right["nearEyeWidth"] * 1.2
    assert right["farToNear"] == pytest.approx(right["farEyeWidth"] / right["nearEyeWidth"], abs=1e-3)
    assert right["farEyeGap"]["turned"] < right["farEyeGap"]["front"]
    assert right["cheekBulge"]["front"] > 0
    for key in ("nearEyeWidth", "farEyeWidth"):
        assert left[key] == pytest.approx(right[key], abs=0.02)
    assert left["farEyeGap"]["turned"] == pytest.approx(right["farEyeGap"]["turned"], abs=0.05)

    pushed = copy.deepcopy(package)
    eye = next(m for m in pushed.model.meshes if m.id == next(p.mesh for p in pushed.model.parts if p.id == "eye_white_l"))
    pushed.model.deformation.keyform_groups.append(
        KeyformGroup("push", eye.id, ["AngleX"], [[0, 45]], "linear", [Keyform([0.0], [[0.0, 0.0]] * len(eye.vertices)), Keyform([45.0], [[6.0, 0.0]] * len(eye.vertices))])
    )
    assert proportions(pushed, 45, 0, shape.radius)["farEyeGap"]["turned"] < right["farEyeGap"]["turned"]


def _face(eyes_x: float, nose_y: float) -> list[dict]:
    points = [[0.0, 0.0]] * 28
    points[0], points[4] = [0.0, 50.0], [100.0, 50.0]
    for i in range(11, 23):
        points[i] = [eyes_x, 40.0]
    points[23] = [eyes_x, nose_y]
    for i in range(24, 28):
        points[i] = [eyes_x, 80.0]
    return [{"bbox": [0, 0, 100, 100, 0.9], "keypoints": [[x, y, 1.0] for x, y in points]}]


@pytest.mark.parametrize("angle_x", [-45, 45])
@pytest.mark.parametrize("angle_y", [-20, 0, 20])
def test_proportions_stay_within_the_review_criteria_on_the_synthetic_face(turned, angle_x, angle_y) -> None:
    package, shape = turned

    result = proportions(package, angle_x, angle_y, shape.radius)

    # 手順書の基準（目標の絵の形）：手前の目は広がらず、奥の目は手前の半分ほどでつぶれず、目尻は輪郭に接しても欠けず、奥側の輪郭は外へ凸。
    assert result["nearEyeWidth"] <= 1.08
    assert 0.5 <= result["farToNear"] <= 0.7
    assert result["farEyeGap"]["turned"] >= 0
    assert result["cheekBulge"]["turned"] > 0


def test_proportions_fall_back_to_the_eyelids_without_eye_whites(turned) -> None:
    package, shape = turned
    without = copy.deepcopy(package)
    for part in without.model.parts:
        if part.role == "eye_white":
            part.role = "eyelid"

    result = proportions(without, 45, 0, shape.radius)

    assert (result["nearEye"], result["farEye"]) == ("eye_white_r", "eye_white_l")
    assert result["farEyeWidth"] < result["nearEyeWidth"]


def test_pose_comparison_gives_the_ratio_of_the_model_turn_to_the_reference_turn() -> None:
    front = _face(50, 60)

    result = pose_comparison(front, _face(60, 60), front, _face(70, 60))

    assert result["render"]["yawDelta"] == pytest.approx(0.1) and result["reference"]["yawDelta"] == pytest.approx(0.2)
    assert result["yawRatio"] == pytest.approx(0.5)
    assert result["pitchRatio"] is None
    assert pose_comparison(front, _face(40, 60), front, _face(70, 60))["yawRatio"] < 0
    assert pose_comparison(None, None, front, _face(70, 60))["render"] is None


def test_review_pack_writes_sheets_and_metrics_for_every_review_angle(turned, tmp_path) -> None:
    if not render.available():
        if os.environ.get("UGOKUE_REQUIRE_DOTNET") == "1":
            pytest.fail("dotnet がない")
        pytest.skip("dotnet がない")
    package, shape = turned
    keyforms = tmp_path / "keyforms"
    save(package, keyforms / "model")
    head = {"center": [float(v) for v in shape.center], "radius": shape.radius, "rows": len(shape.centers)}
    (keyforms / "evaluation.json").write_text(json.dumps({"head": head, "params": load_params(keyforms).to_json()}), encoding="utf-8")
    images = render_images(keyforms)
    references = tmp_path / "references"
    (references / "views").mkdir(parents=True)
    for name, png in images.items():
        (references / "views" / f"{name}.png").write_bytes(png)
    (references / "landmarks.json").write_text(json.dumps({"x+00_y+00": _face(50, 60), "x+45_y+00": _face(70, 60)}), encoding="utf-8")
    (keyforms / "review").mkdir()
    (keyforms / "review" / "render-landmarks.json").write_text(json.dumps({"x+00_y+00": _face(50, 60), "x+45_y+00": _face(65, 60)}), encoding="utf-8")

    metrics = review_pack(keyforms, references)

    assert sorted((a["angleX"], a["angleY"]) for a in metrics["angles"]) == sorted((x, y) for x in REVIEW_X for y in REVIEW_Y)
    assert all(a["reference"] and a["proportions"]["farEye"] for a in metrics["angles"])
    right = next(a for a in metrics["angles"] if (a["angleX"], a["angleY"]) == (45, 0))
    assert right["pose"]["yawRatio"] == pytest.approx(0.75)
    assert metrics["params"] == HeadParams().to_json()
    assert json.loads((keyforms / "review" / "metrics.json").read_text(encoding="utf-8")) == metrics
    for y in REVIEW_Y:
        sheet = Image.open(keyforms / "review" / f"sheet-y{y:+03d}.png")
        assert sheet.height == 2 * 300 and sheet.width > 7 * 100
    for prefix in ("grid", "grid-face"):
        assert all((keyforms / "review" / f"{prefix}-{name}.png").is_file() for name in ("x+00_y+00", "x+45_y+00", "x-45_y-20"))
    assert all((keyforms / "review" / f"sheet-face-y{y:+03d}.png").is_file() for y in REVIEW_Y)
    assert not any(p.role.startswith("hair") for p in load(keyforms / "face-only" / "model").model.parts)
    assert validate_dir(keyforms / "face-only" / "model") == []

    review = _review(sketches=[{"angleX": 45, "angleY": 0, "kind": "contour", "label": "cheek", "points": [[150, 140], [160, 180]], "note": "outward"}])
    (keyforms / "review" / "review-1.json").write_text(json.dumps(review), encoding="utf-8")
    drawn = draw_sketches(keyforms / "review" / "review-1.json")

    assert (keyforms / "review" / "sketch-face-1-x+45_y+00.png").is_file()
    assert drawn["sketches"][0]["distance"]["mean"] >= 0
    assert json.loads((keyforms / "review" / "sketch-face-1.json").read_text(encoding="utf-8")) == drawn


def test_face_only_drops_hair_headwear_and_hands_with_their_children(turned) -> None:
    package, _ = turned

    face = face_only(package)

    roles = {p.role for p in face.model.parts}
    assert "hair_front" not in roles and "hair_back" not in roles and "face" in roles and "eye_white" in roles
    assert {m.id for m in face.model.meshes} == {p.mesh for p in face.model.parts if p.mesh}
    assert {g.mesh for g in face.model.deformation.keyform_groups} <= {m.id for m in face.model.meshes}
    assert set(face.textures) == {m.texture for m in face.model.meshes}
    assert any(p.role.startswith("hair") for p in package.model.parts)


def test_load_params_writes_the_defaults_and_reads_overrides(tmp_path) -> None:
    assert load_params(tmp_path) == HeadParams()
    assert json.loads((tmp_path / "params.json").read_text(encoding="utf-8")) == HeadParams().to_json()

    (tmp_path / "params.json").write_text(json.dumps({"headDepth": 0.7}), encoding="utf-8")
    assert load_params(tmp_path) == HeadParams(head_depth=0.7)
    (tmp_path / "params.json").write_text(json.dumps({"depth": 0.7}), encoding="utf-8")
    with pytest.raises(ValueError, match="unknown"):
        load_params(tmp_path)


def _review(**overrides) -> dict:
    angles = [
        {"angleX": x, "angleY": y, "direction": {"pass": True, "reason": "同じ向き"}, "breakage": {"pass": True, "reason": "崩れなし"}}
        for x in REVIEW_X
        for y in REVIEW_Y
    ]
    data = {"illustration": "01-long-straight", "stage": "face", "iteration": 1, "params": HeadParams().to_json(), "angles": angles, "verdict": "pass", "changes": {}, "summary": "合格"}
    data.update(overrides)
    return data


def _failed(review: dict) -> dict:
    review["angles"][0]["breakage"] = {"pass": False, "reason": "あごが尖る"}
    return review


def test_validate_review_accepts_pass_retry_and_stop() -> None:
    assert validate_review(_review()) == []
    assert validate_review(_failed(_review(verdict="retry", changes={"chinDepth": 0.7}))) == []
    assert validate_review(_failed(_review(iteration=3, verdict="stop"))) == []


@pytest.mark.parametrize(
    "review, message",
    [
        (_review(angles=_review()["angles"][1:]), "must cover x-45_y-20"),
        (_review(angles=_review()["angles"] + _review()["angles"][:1]), "once each"),
        (_review(angles=[{**a, "direction": {"pass": True, "reason": " "}} for a in _review()["angles"]]), "non-empty reason"),
        (_failed(_review()), "every criterion to pass"),
        (_failed(_review(verdict="retry", changes={"chinDepht": 0.7})), "unknown head parameters"),
        (_failed(_review(verdict="retry")), "needs changes"),
        (_failed(_review(iteration=3, verdict="retry", changes={"chinDepth": 0.7})), "must end with pass or stop"),
        (_review(verdict="retry", changes={"chinDepth": 0.7}), "needs a failed criterion"),
        (_failed(_review(verdict="stop", changes={"chinDepth": 0.7})), "must not have changes"),
        (_review(iteration=4), "iteration must be"),
        (_review(verdict="maybe"), "verdict must be"),
        (_review(summary=""), "summary must be"),
        (_review(stage="hair"), "stage must be"),
        (_review(sketches=[{"angleX": 30, "angleY": 0, "kind": "contour", "label": "a", "points": [[0, 0], [1, 1]], "note": "b"}]), "sketches[0] must be at one of"),
    ],
)
def test_validate_review_rejects_inconsistent_reviews(review: dict, message: str) -> None:
    assert any(message in issue for issue in validate_review(review))


def test_the_reference_spec_generates_every_review_angle() -> None:
    spec = load_spec(render.REPO_ROOT / "pipeline" / "samples" / "views-45.json")

    assert sorted((v.angle_x, v.angle_y) for v in spec.views) == sorted((x, y) for x in REVIEW_X for y in REVIEW_Y)
    assert all("{keep}" not in v.prompt and "45 degrees" in v.prompt for v in spec.views)
