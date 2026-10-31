"""頭の3D形状の当てはめと、1つの回転と投影の式で作る顔の向きのキーフォームのテスト。"""

from __future__ import annotations

import copy
import io

import numpy as np
import pytest
from PIL import Image

from ugokue_format.model import Keyform, KeyformGroup
from ugokue_format.package import load
from ugokue_pipeline import render
from ugokue_pipeline.convert import to_model
from ugokue_pipeline.deform import deformed_positions, triangle_quality
from ugokue_pipeline.head3d import ANGLES_X, ANGLES_Y, Attachment, HeadShape, apply_keyforms, attachments_for, build_keyforms, carry_edits, fit_head, turn
from ugokue_pipeline.result import DecompositionResult, Layer

TEST_FACE = render.REPO_ROOT / "format" / "samples" / "test-face"
# 固定テストモデルの頂点の奥行き（test-model.md の表）。
TEST_FACE_LIFT = {"eyelid_r": 2, "eyelid_l": 2, "eyebrow_r": 4, "eyebrow_l": 4, "mouth": 1, "nose": 18, "hair_front": 14, "hair_side_r": -6, "hair_side_l": -6}
TEST_FACE_FLAT = {"ear_r": -30, "ear_l": -30, "hair_back": -35}

# 合成した左右対称の顔。キャンバス 240 × 280、顔は中心 (120, 130)、半幅 60、半高 75 の楕円。
CANVAS = (240, 280)
FACE = (120.0, 130.0, 60.0, 75.0)
EYES = [(96.0, 125.0), (144.0, 125.0)]
NOSE = (120.0, 152.0)
MOUTH = (120.0, 172.0)


def _ellipse(box: tuple[int, int, int, int], color: tuple[int, int, int]) -> np.ndarray:
    """箱に内接する楕円の RGBA 画像。画素の中心で判定するので、左右対称になる。"""
    x1, y1, x2, y2 = box
    ys, xs = np.mgrid[y1:y2, x1:x2] + 0.5
    cx, cy, a, b = (x1 + x2) / 2, (y1 + y2) / 2, (x2 - x1) / 2, (y2 - y1) / 2
    image = np.zeros((y2 - y1, x2 - x1, 4), dtype=np.uint8)
    image[..., :3] = color
    image[..., 3] = np.where(((xs - cx) / a) ** 2 + ((ys - cy) / b) ** 2 <= 1, 255, 0)
    return image


def _box(cx: float, cy: float, half_w: float, half_h: float) -> tuple[int, int, int, int]:
    return round(cx - half_w), round(cy - half_h), round(cx + half_w), round(cy + half_h)


def synthetic_face():
    """左右対称の合成した顔のモデルと、正面の特徴点。"""
    cx, cy, a, b = FACE
    specs = [
        ("back hair", _box(cx, cy + 20, 72, 110), 0.9),
        ("neck", _box(cx, 215, 20, 25), 0.85),
        ("ears-r", _box(cx - a - 4, cy + 5, 8, 14), 0.75),
        ("ears-l", _box(cx + a + 4, cy + 5, 8, 14), 0.75),
        ("face", _box(cx, cy, a, b), 0.7),
        ("nose", _box(*NOSE, 4, 6), 0.66),
        ("mouth", _box(*MOUTH, 10, 3), 0.66),
        ("eyewhite-r", _box(*EYES[0], 12, 7), 0.65),
        ("eyewhite-l", _box(*EYES[1], 12, 7), 0.65),
        ("irides-r", _box(*EYES[0], 5, 6), 0.64),
        ("irides-l", _box(*EYES[1], 5, 6), 0.64),
        ("eyebrow-r", _box(EYES[0][0], 108, 13, 3), 0.63),
        ("eyebrow-l", _box(EYES[1][0], 108, 13, 3), 0.63),
        ("front hair", _box(cx, 85, 64, 32), 0.4),
    ]
    layers = [Layer(tag, box, depth, _ellipse(box, (200, 150, 120))) for tag, box, depth in specs]
    package = to_model(DecompositionResult(CANVAS[0], CANVAS[1], layers, None))
    landmarks = {}
    for side, (ex, ey) in enumerate(EYES):
        for k in range(6):
            angle = 2 * np.pi * k / 6
            landmarks[f"afd_{11 + 6 * side + k:02d}"] = (ex + 10 * np.cos(angle), ey + 5 * np.sin(angle))
    landmarks["afd_23"] = NOSE
    for k, dx in enumerate((-8, -3, 3, 8)):
        landmarks[f"afd_{24 + k:02d}"] = (MOUTH[0] + dx, MOUTH[1])
    return package, landmarks


@pytest.fixture(scope="module")
def synthetic():
    package, landmarks = synthetic_face()
    shape = fit_head(package, landmarks)
    return package, shape, build_keyforms(package, shape)


def _mesh(package, part_id):
    part = next(p for p in package.model.parts if p.id == part_id)
    return next(m for m in package.model.meshes if m.id == part.mesh)


def _opaque(package, mesh) -> np.ndarray:
    alpha = np.array(Image.open(io.BytesIO(package.textures[mesh.texture])).convert("RGBA"))[..., 3]
    uv = np.array(mesh.uvs)
    h, w = alpha.shape
    return alpha[np.clip((uv[:, 1] * (h - 1)).round().astype(int), 0, h - 1), np.clip((uv[:, 0] * (w - 1)).round().astype(int), 0, w - 1)] > 0


def _positions(package, groups, part_id, x, y) -> np.ndarray:
    mesh = _mesh(package, part_id)
    return deformed_positions(mesh, groups, {"AngleX": x, "AngleY": y})


def test_sphere_setting_matches_the_hand_made_keyforms() -> None:
    package = load(TEST_FACE)
    shape = HeadShape.sphere((256, 236), 120, 0.5)
    attachments = {part.id: Attachment("flat", depth=TEST_FACE_FLAT[part.id]) if part.id in TEST_FACE_FLAT else Attachment(lift=TEST_FACE_LIFT.get(part.id, 0))
                   for part in package.model.parts if part.mesh is not None}

    groups = build_keyforms(package, shape, attachments, [-30, 0, 30], [-30, 0, 30], pivot_depth=0, silhouette=False, neck=False, pitch_scale=1.0)

    truth = {g.mesh: g for g in package.model.deformation.keyform_groups if g.parameters == ["AngleX", "AngleY"]}
    assert {g.mesh for g in groups} == set(truth)
    for group in groups:
        expected = {tuple(k.at): np.array(k.offsets) for k in truth[group.mesh].keyforms}
        for keyform in group.keyforms:
            assert np.allclose(np.array(keyform.offsets), expected[tuple(keyform.at)], atol=0.01), (group.mesh, keyform.at)


def test_template_fits_the_face_layer_and_landmarks(synthetic) -> None:
    package, shape, _ = synthetic
    cx, cy, a, b = FACE

    assert shape.radius == pytest.approx(a, rel=0.03)
    assert np.allclose(shape.center, (cx, cy), atol=1)
    assert np.allclose(shape.centers, cx, atol=0.5)
    _, middle, depth = shape.section(np.array([cy]))
    assert middle[0] == pytest.approx(a, rel=0.03) and depth[0] == pytest.approx(0.8 * a, rel=0.05)
    # 口より下は、口の行の幅のままのあごの板で、奥行きがあご先の値へ近づく（あごの輪郭の上でも奥行きが 0 にならない）。
    _, mouth_width, mouth_depth = shape.section(np.array([MOUTH[1]]))
    _, width, depth = shape.section(np.array([cy + b]))
    assert width[0] == pytest.approx(mouth_width[0]) and depth[0] == pytest.approx(0.55 * shape.radius, rel=0.02)
    assert mouth_depth[0] > depth[0]


@pytest.mark.parametrize("kind", ["face", "hair_front", "nose", "eye_white_r", "ear_r", "hair_back"])
@pytest.mark.parametrize("angle", [(15, 0), (30, 20), (45, -20)])
def test_turning_left_and_right_are_mirror_images(synthetic, kind, angle) -> None:
    package, shape, _ = synthetic
    attachment = attachments_for(package.model, shape.radius)[kind]
    rest = np.array(_mesh(package, kind).vertices, dtype=np.float64)
    mirrored = rest * [-1, 1] + [2 * FACE[0], 0]
    yaw, pitch = angle

    right = turn(shape, rest, attachment, yaw, pitch, pivot=10)
    left = turn(shape, mirrored, attachment, -yaw, pitch, pivot=10)

    assert np.allclose(left * [-1, 1] + [2 * FACE[0], 0], right, atol=0.05)


def _width(points: np.ndarray) -> float:
    return float(points[:, 0].max() - points[:, 0].min())


def test_far_eye_narrows_toward_its_outer_corner_and_near_eye_keeps_its_width(synthetic) -> None:
    package, _, groups = synthetic
    # AngleX が正だと顔の前面が画像の右へ動き、画像の右の目（eye_white_l）が奥になる。
    far_rest, near_rest = (np.array(_mesh(package, p).vertices) for p in ("eye_white_l", "eye_white_r"))
    far, near = _positions(package, groups, "eye_white_l", 30, 0), _positions(package, groups, "eye_white_r", 30, 0)

    assert _width(near) / _width(near_rest) > 0.9
    assert _width(far) / _width(far_rest) < 0.85
    assert _width(far) < _width(near)
    # 奥の目は、目尻（外側）の側ほど縮む。
    middle = far_rest[:, 0].mean()
    outer, inner = far_rest[:, 0] > middle, far_rest[:, 0] <= middle
    assert _width(far[outer]) / _width(far_rest[outer]) < _width(far[inner]) / _width(far_rest[inner])


def _center_motion(package, groups, part_id, x, y) -> np.ndarray:
    rest = np.array(_mesh(package, part_id).vertices)
    return (_positions(package, groups, part_id, x, y) - rest).mean(axis=0)


def test_nose_tip_moves_more_than_the_mouth_and_cheek(synthetic) -> None:
    package, shape, groups = synthetic

    nose = _center_motion(package, groups, "nose", 30, 0)[0]
    mouth = _center_motion(package, groups, "mouth", 30, 0)[0]
    face = _center_motion(package, groups, "face", 30, 0)[0]

    assert nose > mouth > face > 0
    assert nose - mouth > 0.05 * shape.radius


def test_chin_moves_toward_the_turn(synthetic) -> None:
    package, shape, groups = synthetic
    face = _mesh(package, "face")
    rest = np.array(face.vertices)
    opaque = _opaque(package, face)
    chin = int(np.argmax(np.where(opaque, rest[:, 1], -np.inf)))
    cheeks = opaque & (np.abs(rest[:, 1] - FACE[1]) <= 4)

    moved = _positions(package, groups, "face", 30, 0)
    outline = (moved[cheeks, 0].min() + moved[cheeks, 0].max()) / 2

    # あご先は、頬の輪郭の中央より、向いた側へ寄る。
    assert moved[chin, 0] - outline > 0.1 * shape.radius


def test_front_view_is_unchanged(synthetic) -> None:
    package, _, groups = synthetic

    for group in groups:
        zero = next(k for k in group.keyforms if k.at == [0, 0])
        assert np.abs(np.array(zero.offsets)).max() < 1e-6, group.mesh


def _inside_face(face: np.ndarray, points: np.ndarray, band: float) -> np.ndarray:
    """各点の行（±band）に、左右両側とも顔の不透明な頂点があるか。"""
    result = np.zeros(len(points), dtype=bool)
    for i, (x, y) in enumerate(points):
        row = face[np.abs(face[:, 1] - y) <= band]
        result[i] = len(row) > 0 and row[:, 0].min() <= x <= row[:, 0].max()
    return result


@pytest.mark.parametrize("part_id", ["eye_white_r", "eye_white_l", "iris_r", "iris_l", "eyebrow_r", "eyebrow_l"])
def test_eyes_and_brows_stay_inside_the_contour_at_every_angle(synthetic, part_id) -> None:
    package, _, groups = synthetic
    face_mesh, mesh = _mesh(package, "face"), _mesh(package, part_id)
    face_opaque, opaque = _opaque(package, face_mesh), _opaque(package, mesh)

    for x in range(-45, 46, 5):
        for y in range(-20, 21, 10):
            face = _positions(package, groups, "face", x, y)[face_opaque]
            points = _positions(package, groups, part_id, x, y)[opaque]
            assert _inside_face(face, points, band=4).all(), (x, y)


def test_no_flipped_or_extremely_stretched_triangles_between_grid_points(synthetic) -> None:
    package, _, groups = synthetic
    meshes = {m.id: m for m in package.model.meshes}

    for group in groups:
        mesh = meshes[group.mesh]
        rest = np.array(mesh.vertices)
        for x in range(-45, 46, 5):
            for y in range(-20, 21, 5):
                quality = triangle_quality(rest, deformed_positions(mesh, [group], {"AngleX": x, "AngleY": y}), mesh.triangles)
                assert quality["flipped"] == 0, (group.mesh, x, y)
                if group.mesh != "face" and not group.mesh.startswith("hair"):
                    assert 1 / 3 <= quality["minStretch"] and quality["maxStretch"] <= 3, (group.mesh, x, y, quality)


def test_neck_top_follows_the_head_and_bottom_stays(synthetic) -> None:
    package, _, groups = synthetic
    rest = np.array(_mesh(package, "neck").vertices)

    moved = _positions(package, groups, "neck", 30, 0) - rest

    top, bottom = rest[:, 1] == rest[:, 1].min(), rest[:, 1] == rest[:, 1].max()
    assert moved[top, 0].mean() > 3 and np.abs(moved[bottom]).max() < 1e-6


def test_keyforms_cover_the_grid_and_widen_the_angle_range(synthetic) -> None:
    package, _, groups = synthetic

    apply_keyforms(package, groups)

    assert all(g.keys == [ANGLES_X, ANGLES_Y] for g in groups)
    angle_x = next(p for p in package.model.parameters if p.id == "AngleX")
    assert (angle_x.min, angle_x.max) == (-45, 45)


def _edit_group(package, mesh_id, keys, offset=(1.5, -2.0)):
    """手の修正のグループ（Unity エディタのキーフォームエディタが作るもの）を、1つの格子点だけ動かして作る。"""
    count = len(_mesh(package, mesh_id).vertices)
    keyforms = [Keyform([x, y], [list(offset) if (x, y) == (30, 0) else [0.0, 0.0] for _ in range(count)]) for x in keys[0] for y in keys[1]]
    return KeyformGroup(f"{mesh_id}_angle_edit", mesh_id, ["AngleX", "AngleY"], keys, "linear", keyforms)


def test_regenerating_keeps_hand_edits(synthetic) -> None:
    package, shape, groups = synthetic
    previous = copy.deepcopy(package)
    apply_keyforms(previous, groups)
    previous.model.deformation.keyform_groups.append(_edit_group(previous, "nose", [ANGLES_X, ANGLES_Y]))
    regenerated = copy.deepcopy(package)
    apply_keyforms(regenerated, build_keyforms(regenerated, shape))

    dropped = carry_edits(previous.model, regenerated)
    apply_keyforms(regenerated, groups)

    assert dropped == []
    ids = [g.id for g in regenerated.model.deformation.keyform_groups]
    assert ids.count("nose_angle_edit") == 1 and ids.count("nose_angle") == 1
    mesh = _mesh(regenerated, "nose")
    automatic = deformed_positions(mesh, [g for g in groups if g.mesh == "nose"], {"AngleX": 22.5, "AngleY": 0})
    edited = deformed_positions(mesh, regenerated.model.deformation.keyform_groups, {"AngleX": 22.5, "AngleY": 0})
    assert np.allclose(edited - automatic, [0.75, -1.0])


def test_edits_for_changed_meshes_or_grids_are_dropped(synthetic) -> None:
    package, shape, groups = synthetic
    previous = copy.deepcopy(package)
    apply_keyforms(previous, groups)
    previous.model.deformation.keyform_groups.append(_edit_group(previous, "nose", [[-30, 0, 30], [-30, 0, 30]]))
    previous.model.deformation.keyform_groups.append(_edit_group(previous, "mouth", [ANGLES_X, ANGLES_Y]))
    previous.model.deformation.keyform_groups.append(_edit_group(previous, "face", [ANGLES_X, ANGLES_Y]))
    next(m for m in previous.model.meshes if m.id == "mouth").vertices[0][0] += 1
    regenerated = copy.deepcopy(package)
    apply_keyforms(regenerated, build_keyforms(regenerated, shape))

    dropped = carry_edits(previous.model, regenerated)

    assert sorted(dropped) == ["mouth_angle_edit", "nose_angle_edit"]
    assert [g.id for g in regenerated.model.deformation.keyform_groups if g.id.endswith("_edit")] == ["face_angle_edit"]
