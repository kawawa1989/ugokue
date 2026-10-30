"""固定テストモデル（format/samples/test-face）が、以降のチケットの入力として必要な内容を持つことのテスト。"""

from __future__ import annotations

import itertools

import pytest
from conftest import TEST_FACE_DIR

from ugokue_format.model import KeyformGroup, Model
from ugokue_format.package import load
from ugokue_format.validate import validate_dir

ANGLE_KEYS = [-30, 0, 30]
FACE_ROLES = {"face", "hair_front", "hair_side", "hair_back", "eyebrow", "eyelid", "eye_white", "iris", "mouth", "mouth_inner", "teeth", "nose", "ear"}


@pytest.fixture(scope="module")
def model() -> Model:
    """コミット済みの固定テストモデルを読み込む。"""
    return load(TEST_FACE_DIR).model


def head_meshes(model: Model) -> list[str]:
    """head パーツの子孫が持つメッシュの ID を返す。"""
    parents = {p.id: p.parent for p in model.parts}

    def under_head(part: str | None) -> bool:
        while part is not None:
            if part == "head":
                return True
            part = parents[part]
        return False

    return [m.id for m in model.meshes if under_head(m.part)]


def groups_for(model: Model, mesh: str, parameters: list[str]) -> list[KeyformGroup]:
    """メッシュとパラメータの組が一致するキーフォームグループを返す。"""
    return [g for g in model.deformation.keyform_groups if g.mesh == mesh and g.parameters == parameters]


def test_test_face_is_valid() -> None:
    assert validate_dir(TEST_FACE_DIR) == []


def test_has_about_twenty_face_parts(model: Model) -> None:
    assert len(model.parts) >= 20
    assert FACE_ROLES <= {p.role for p in model.parts}


def test_every_head_mesh_has_angle_grid(model: Model) -> None:
    meshes = head_meshes(model)
    assert len(meshes) >= 18
    for mesh in meshes:
        (group,) = groups_for(model, mesh, ["AngleX", "AngleY"])
        assert group.keys == [ANGLE_KEYS, ANGLE_KEYS]
        assert sorted(tuple(k.at) for k in group.keyforms) == list(itertools.product(ANGLE_KEYS, ANGLE_KEYS))


@pytest.mark.parametrize(
    ("parameter", "meshes"),
    [
        ("EyeOpenL", ["eye_white_l", "iris_l", "eyelid_l"]),
        ("EyeOpenR", ["eye_white_r", "iris_r", "eyelid_r"]),
        ("MouthOpenY", ["mouth_inner", "teeth", "mouth", "face"]),
    ],
)
def test_blink_and_mouth_move_their_meshes(model: Model, parameter: str, meshes: list[str]) -> None:
    for mesh in meshes:
        (group,) = groups_for(model, mesh, [parameter])
        assert group.keys == [[0, 1]]
        assert any(dx or dy for k in group.keyforms for dx, dy in k.offsets)


def test_default_parameters_give_base_shape(model: Model) -> None:
    defaults = {p.id: p.default for p in model.parameters}
    for group in model.deformation.keyform_groups:
        at = [defaults[p] for p in group.parameters]
        (keyform,) = [k for k in group.keyforms if k.at == at]
        assert all(dx == 0 and dy == 0 for dx, dy in keyform.offsets), group.id


def test_views_cover_angle_grid(model: Model) -> None:
    assert sorted((v.yaw, v.pitch) for v in model.views) == list(itertools.product(ANGLE_KEYS, ANGLE_KEYS))


def test_landmarks_exist_in_every_view_where_visible(model: Model) -> None:
    ids = {lm.id for lm in model.correspondence.landmarks}
    part_of = {lm.id: lm.part for lm in model.correspondence.landmarks}
    for view in model.views:
        hidden = {o.part for o in view.overrides if o.visible is False}
        present = {lm.id for lm in model.correspondence.landmarks if lm.view == view.id}
        assert present == {i for i in ids if part_of[i] not in hidden}, view.id


def test_vertex_matches_follow_angle_keyforms(model: Model) -> None:
    views = {v.id: (v.yaw, v.pitch) for v in model.views}
    meshes = {m.id: m for m in model.meshes}
    matches = model.correspondence.vertex_matches
    assert matches
    for match in matches:
        (group,) = groups_for(model, match.mesh, ["AngleX", "AngleY"])
        (keyform,) = [k for k in group.keyforms if tuple(k.at) == views[match.view]]
        x, y = meshes[match.mesh].vertices[match.vertex]
        dx, dy = keyform.offsets[match.vertex]
        assert match.position == pytest.approx([x + dx, y + dy], abs=0.011)
