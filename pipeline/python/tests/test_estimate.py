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
from ugokue_pipeline.estimate import ViewInput, estimate_keyforms, head_depth, head_parts, on_head_surface, owner_map, solve_offsets

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


def test_solve_keeps_an_uneven_prior_without_targets() -> None:
    rest, triangles = QUAD
    prior = np.array([[0.0, 0.0], [10.0, 0.0], [10.0, 5.0], [0.0, 5.0]])

    offsets = solve_offsets(rest, triangles, rest, np.zeros(4), prior, prior_weight=1.0, keep_prior_shape=True)

    # 予測の形を保つときは、滑らかさは予測からのずれにかかるので、場所によって違う予測の動きはならされない。
    assert np.allclose(offsets, prior)


def test_parts_drawn_in_front_of_the_face_lie_on_the_head_surface() -> None:
    depths = {"face": 0.0, "hair_front": -5.0, "hair_back": 5.0, "ear_l": 3.0}

    assert on_head_surface(depths, {"hair_front", "hair_back", "ear_l"}) == {"hair_front"}
    assert on_head_surface({"hair_front": -5.0, "hair_back": 5.0}, {"hair_front", "hair_back"}) == set()


def test_head_depth_inflates_the_face_layer() -> None:
    package = load(TEST_FACE)
    center, depth, inner = head_depth(package)
    owner, ids = owner_map(package)

    # 輪郭の外は 0、内側ほど手前で、いちばん内側では 0.5 × r（円なら球と同じ）。
    assert depth.shape == owner.shape and depth[0, 0] == 0
    assert np.isclose(depth.max(), 0.5 * inner, rtol=0.02)
    cx, cy = np.round(center).astype(int)
    assert depth[cy, cx] > depth[cy, cx + int(0.8 * inner)] > 0


def test_turning_narrows_the_side_the_face_turns_toward() -> None:
    from ugokue_pipeline.correspond import rotate_with_depth

    center, depth, inner = head_depth(load(TEST_FACE))
    xs = np.nonzero(depth[int(center[1])] > 0)[0]
    points = np.array([[xs.min(), center[1]], center, [xs.max(), center[1]]], dtype=np.float64)
    z = np.array([depth[int(y), int(x)] for x, y in points])

    # 右を向くと、顔の中心は輪郭より大きく右へ動き、右半分は狭く、左半分は広くなる（輪郭は奥行き 0 の頭の外形として残る）。
    left, middle, right = rotate_with_depth(points, center, z, 30, 0)
    assert middle[0] - center[0] > 0.2 * inner
    assert right[0] - middle[0] < 0.8 * (points[2, 0] - points[1, 0])
    assert middle[0] - left[0] > points[1, 0] - points[0, 0]


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
def test_face_inputs(tmp_path_factory: pytest.TempPathFactory):
    """固定テストモデルを正面と各向きで描いた絵と、特徴点。手作りの顔の向きのキーフォームを外したモデルも返す。"""
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
    return package, stripped, front, landmarks[views_by_angle[(0, 0)]], inputs


@pytest.fixture(scope="module")
def test_face_estimate(test_face_inputs):
    """固定テストモデルの描いた絵から、顔の向きのキーフォームを推定する（奥行きなし）。"""
    package, stripped, front, front_landmarks, inputs = test_face_inputs
    return package, estimate_keyforms(stripped, front, front_landmarks, inputs)


# 固定テストモデルの奥行き（test-model.md の表。頭の中心で、手前が正）を、See-through と同じ「小さいほど手前」にしたもの。
TEST_FACE_DEPTHS = {"hair_front": -(60 + 14), "hair_side_r": -(60 - 6), "hair_side_l": -(60 - 6), "ear_r": 30, "ear_l": 30, "hair_back": 35}


@pytest.fixture(scope="module")
def test_face_estimate_with_depth(test_face_inputs):
    """奥行きを渡して推定する。"""
    package, stripped, front, front_landmarks, inputs = test_face_inputs
    return package, estimate_keyforms(stripped, front, front_landmarks, inputs, part_depths={**TEST_FACE_DEPTHS, "face": -60})


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


def test_depth_moves_parts_behind_the_face_the_right_way(test_face_estimate, test_face_estimate_with_depth) -> None:
    without = _errors(test_face_estimate)
    with_depth = _errors(test_face_estimate_with_depth)

    for mesh in TEST_FACE_DEPTHS:
        assert with_depth[mesh][0] < without[mesh][0], f"{mesh}: {with_depth[mesh][0]:.1f} px with depth, {without[mesh][0]:.1f} px without"
    for mesh in FACE_FEATURES:
        error, movement = with_depth[mesh]
        assert error <= 0.25 * movement, f"{mesh}: error {error:.1f} px, movement {movement:.1f} px"
    print({m: (round(without[m][0], 1), round(with_depth[m][0], 1), round(with_depth[m][1], 1)) for m in [*TEST_FACE_DEPTHS, "face", *FACE_FEATURES]})
