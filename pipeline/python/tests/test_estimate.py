"""頂点の移動量の推定と、固定テストモデルの手作りのキーフォームを正解とした比較のテスト。"""

from __future__ import annotations

import copy
import os
from pathlib import Path

import numpy as np
import pytest

from ugokue_format.package import load
from ugokue_pipeline import render
from ugokue_pipeline.deform import deformed_positions
from ugokue_pipeline.estimate import ViewInput, estimate_keyforms, head_parts, owner_map, solve_offsets

TEST_FACE = render.REPO_ROOT / "format" / "samples" / "test-face"
QUAD = (np.array([[0.0, 0.0], [10.0, 0.0], [10.0, 10.0], [0.0, 10.0]]), [[0, 1, 2], [0, 2, 3]])


def test_solve_reaches_consistent_targets() -> None:
    rest, triangles = QUAD
    shift = np.array([3.0, -2.0])

    offsets = solve_offsets(rest, triangles, rest + shift, np.ones(4), np.tile(shift, (4, 1)))

    assert np.allclose(offsets, shift)


def test_solve_fills_unmatched_vertices_smoothly() -> None:
    rest, triangles = QUAD
    targets = rest + [4.0, 0.0]
    weights = np.array([1.0, 1.0, 1.0, 0.0])

    offsets = solve_offsets(rest, triangles, targets, weights, np.zeros((4, 2)))

    # 対応のない頂点は、となりの頂点に引かれて同じ向きに動く。
    assert offsets[3, 0] > 2.0


def test_head_parts_and_owner_map_of_the_test_face() -> None:
    package = load(TEST_FACE)

    parts = head_parts(package.model)
    owner, ids = owner_map(package)

    assert "face" in parts and "hair_back" in parts
    assert "body" not in parts and "neck" not in parts
    assert owner[0, 0] == -1
    assert ids[owner[300, 256]] == "face"


def _grid_values(group_keys: list[list[float]]) -> list[tuple[int, int]]:
    return [(int(x), int(y)) for x in group_keys[0] for y in group_keys[1] if (x, y) != (0, 0)]


@pytest.fixture(scope="module")
def test_face_estimate(tmp_path_factory: pytest.TempPathFactory):
    """固定テストモデルを各向きで描いた絵から、顔の向きのキーフォームを推定する。"""
    if not render.available():
        if os.environ.get("UGOKUE_REQUIRE_DOTNET") == "1":
            pytest.fail("dotnet がない")
        pytest.skip("dotnet がない")
    package = load(TEST_FACE)
    directory = tmp_path_factory.mktemp("renders")
    landmarks: dict[str, dict[str, tuple[float, float]]] = {}
    for landmark in package.model.correspondence.landmarks:
        landmarks.setdefault(landmark.view, {})[landmark.id] = tuple(landmark.position)
    views_by_angle = {(int(v.yaw), int(v.pitch)): v.id for v in package.model.views}
    front = render.render(TEST_FACE, directory / "front.png", {})
    inputs = []
    for x, y in _grid_values([[-30, 0, 30], [-30, 0, 30]]):
        image = render.render(TEST_FACE, directory / f"{x}_{y}.png", {"AngleX": x, "AngleY": y})
        inputs.append(ViewInput(x, y, image, landmarks[views_by_angle[(x, y)]]))
    stripped = copy.deepcopy(package)
    stripped.model.deformation.keyform_groups = [g for g in package.model.deformation.keyform_groups if g.parameters != ["AngleX", "AngleY"]]
    return package, estimate_keyforms(stripped, front, landmarks[views_by_angle[(0, 0)]], inputs)


# 顔の中のパーツ。正面の絵で輪郭や色の境目が見えるので、対応点がとれる。
FACE_FEATURES = ["eye_white_r", "eye_white_l", "iris_r", "iris_l", "eyelid_r", "eyelid_l", "eyebrow_r", "eyebrow_l", "nose", "mouth", "mouth_inner", "teeth"]


def _opaque_vertices(package, mesh) -> np.ndarray:
    """テクスチャの不透明な画素の上にある頂点（描画に効く頂点）。"""
    import io

    from PIL import Image

    alpha = np.array(Image.open(io.BytesIO(package.textures[mesh.texture])).convert("RGBA"))[..., 3]
    uv = np.array(mesh.uvs)
    h, w = alpha.shape
    return alpha[np.clip((uv[:, 1] * (h - 1)).round().astype(int), 0, h - 1), np.clip((uv[:, 0] * (w - 1)).round().astype(int), 0, w - 1)] > 0


def _errors(test_face_estimate) -> dict[str, tuple[float, float]]:
    """メッシュごとに、手作りのキーフォームとの誤差と、変形しない場合の誤差（どちらも描画に効く頂点の平均、画素）。"""
    package, estimate = test_face_estimate
    truth = {g.mesh: g for g in package.model.deformation.keyform_groups if g.parameters == ["AngleX", "AngleY"]}
    meshes = {m.id: m for m in package.model.meshes}
    result = {}
    for group in estimate.groups:
        mesh = meshes[group.mesh]
        opaque = _opaque_vertices(package, mesh)
        errors, movements = [], []
        for x, y in _grid_values(group.keys):
            expected = deformed_positions(mesh, [truth[mesh.id]], {"AngleX": x, "AngleY": y})
            actual = deformed_positions(mesh, [group], {"AngleX": x, "AngleY": y})
            errors.append(np.linalg.norm(expected - actual, axis=1)[opaque].mean())
            movements.append(np.linalg.norm(expected - np.array(mesh.vertices), axis=1)[opaque].mean())
        result[mesh.id] = (float(np.mean(errors)), float(np.mean(movements)))
    return result


def test_estimate_covers_every_head_mesh(test_face_estimate) -> None:
    package, estimate = test_face_estimate
    truth = {g.mesh for g in package.model.deformation.keyform_groups if g.parameters == ["AngleX", "AngleY"]}

    assert {g.mesh for g in estimate.groups} == truth


def test_face_features_follow_the_hand_made_keyforms(test_face_estimate) -> None:
    errors = _errors(test_face_estimate)

    for mesh in FACE_FEATURES:
        error, movement = errors[mesh]
        assert error <= 0.25 * movement, f"{mesh}: error {error:.1f} px, movement {movement:.1f} px"
    face_error, face_movement = errors["face"]
    assert face_error < face_movement


def test_estimated_keyforms_do_not_flip_triangles(test_face_estimate) -> None:
    from ugokue_pipeline.evaluate import mesh_quality

    package, estimate = test_face_estimate
    model = copy.deepcopy(package.model)
    model.deformation.keyform_groups = estimate.groups

    quality = mesh_quality(model, [g.mesh for g in estimate.groups], list(range(-30, 31, 5)), list(range(-30, 31, 5)))

    assert quality["flippedTriangles"] == 0


def test_flips_are_resolved_by_stronger_smoothness() -> None:
    from ugokue_pipeline.deform import triangle_quality
    from ugokue_pipeline.estimate import solve_without_flips

    rest, triangles = QUAD
    targets = rest.copy()
    targets[2] = [-10.0, -10.0]  # 1つの頂点だけが反対側へ抜ける対応点
    weights = np.array([1.0, 1.0, 1.0, 1.0])

    assert triangle_quality(rest, rest + solve_offsets(rest, triangles, targets, weights, np.zeros((4, 2)), 0.0), triangles)["flipped"] > 0
    offsets = solve_without_flips(rest, triangles, targets, weights, np.zeros((4, 2)))
    assert triangle_quality(rest, rest + offsets, triangles)["flipped"] == 0
