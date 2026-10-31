"""対応点の推定（大きさと傾きの補正、薄板スプライン、オプティカルフロー）のテスト。"""

from __future__ import annotations

import math

import numpy as np
from PIL import Image, ImageDraw

from ugokue_pipeline.correspond import apply_affine, correspond, fit_similarity, normalizing_transform, rotate_rigid


def test_rigid_rotation_moves_the_front_of_the_head_toward_the_turn() -> None:
    center = np.array([100.0, 100.0])
    nose = np.array([[100.0, 100.0]])

    assert np.allclose(rotate_rigid(nose, center, 50, 0, 0)[0], [100.0, 100.0])
    assert rotate_rigid(nose, center, 50, 30, 0)[0, 0] > 100  # 画面の右を向く
    assert rotate_rigid(nose, center, 50, 0, 30)[0, 1] < 100  # 上を向く


def test_similarity_recovers_scale_rotation_and_shift() -> None:
    source = np.array([[0.0, 0.0], [10.0, 0.0], [0.0, 20.0], [7.0, 3.0]])
    angle = math.radians(15)
    rotation = np.array([[math.cos(angle), -math.sin(angle)], [math.sin(angle), math.cos(angle)]])
    target = source @ (1.5 * rotation).T + [4, -2]

    matrix = fit_similarity(source, target)

    assert np.allclose(apply_affine(matrix, source), target)


def test_normalizing_undoes_a_shrunk_and_tilted_view() -> None:
    front = {str(i): (100 + 30 * math.cos(i), 100 + 40 * math.sin(i)) for i in range(8)}
    expected = rotate_rigid(np.array(list(front.values())), np.mean(list(front.values()), axis=0), 0, 0, 0)
    shrink = np.array([[0.7 * math.cos(0.3), -0.7 * math.sin(0.3), 20], [0.7 * math.sin(0.3), 0.7 * math.cos(0.3), 5]])
    view = {k: tuple(apply_affine(shrink, np.array([v]))[0]) for k, v in front.items()}

    matrix = normalizing_transform(front, view, 0, 0)

    restored = apply_affine(matrix, np.array([view[k] for k in front]))
    assert np.allclose(restored, expected, atol=1e-6)


def _scene(shift: int) -> tuple[np.ndarray, dict[str, tuple[float, float]]]:
    """テクスチャのある円（顔の代わり）を shift 画素右に置いた画像と、その特徴点。"""
    image = Image.new("RGB", (160, 160), (255, 255, 255))
    draw = ImageDraw.Draw(image)
    draw.ellipse((40 + shift, 40, 120 + shift, 120), fill=(240, 200, 180), outline=(60, 40, 30), width=3)
    for i in range(6):
        draw.ellipse((55 + 9 * i + shift, 70, 61 + 9 * i + shift, 76), fill=(30 + 30 * i, 60, 140))
    draw.line((60 + shift, 100, 100 + shift, 104), fill=(150, 50, 60), width=3)
    points = {"l": (55.0, 73.0), "r": (105.0, 73.0), "m": (80.0, 102.0), "t": (80.0, 42.0), "b": (80.0, 118.0)}
    return np.array(image), {k: (x + shift, y) for k, (x, y) in points.items()}


def test_correspondence_follows_a_shifted_face() -> None:
    front, front_landmarks = _scene(0)
    view, view_landmarks = _scene(6)
    region = np.zeros(front.shape[:2], dtype=bool)
    region[45:115, 45:115] = True

    # 平行移動は大きさと傾きの補正で取り除かれるので、対応はほぼ恒等になる。
    result = correspond(front, view, front_landmarks, view_landmarks, 0, 0, region)

    ys, xs = np.nonzero(result.confidence > 0.5)
    assert len(xs) > 0.5 * region.sum()
    assert np.abs(result.target_x[ys, xs] - xs).mean() < 1.0
    assert np.abs(result.target_y[ys, xs] - ys).mean() < 1.0
    assert result.confidence[0, 0] == 0



def test_normalizing_ignores_implausible_rotation_but_keeps_scale() -> None:
    front = {str(i): (100 + 30 * math.cos(i), 100 + 40 * math.sin(i)) for i in range(8)}
    shrunk_and_turned = np.array([[0.6 * math.cos(1.2), -0.6 * math.sin(1.2), 0], [0.6 * math.sin(1.2), 0.6 * math.cos(1.2), 0]])
    view = {k: tuple(apply_affine(shrunk_and_turned, np.array([v]))[0]) for k, v in front.items()}

    matrix = normalizing_transform(front, view, 0, 0)

    # 69° の回転は検出の外れとみなして使わず、0.6 倍に縮んだ分だけを戻す。
    assert np.allclose(matrix[:, :2], matrix[0, 0] * np.eye(2))
    assert 1.3 < matrix[0, 0] < 2.0


def test_scale_is_kept_within_range() -> None:
    source = np.array([[0.0, 0.0], [10.0, 0.0], [0.0, 10.0]])

    matrix = fit_similarity(source, source * 5, scale_range=(0.5, 2.0))

    assert np.isclose(matrix[0, 0], 2.0)


def test_normalizing_uses_only_stable_landmarks() -> None:
    front = {str(i): (100 + 30 * math.cos(i), 100 + 40 * math.sin(i)) for i in range(8)}
    view = dict(front)
    view["0"] = (400.0, 400.0)  # 外れた検出

    matrix = normalizing_transform(front, view, 0, 0, stable={str(i) for i in range(1, 8)})

    assert np.allclose(matrix, [[1, 0, 0], [0, 1, 0]], atol=1e-6)
