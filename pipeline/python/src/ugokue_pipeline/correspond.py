"""正面の絵と、向きを変えた絵の対応点を求める。特徴点で大きさと傾きをそろえ、薄板スプラインでおおまかに合わせ、オプティカルフローで細かく合わせる。"""

from __future__ import annotations

import math
from dataclasses import dataclass

import cv2
import numpy as np
from scipy.interpolate import RBFInterpolator

# 頭を、特徴点の広がりのこの倍の半径の球を奥行き方向に半分にした楕円体とみなす（固定テストモデルと同じ形）。
HEAD_RADIUS_SCALE = 1.15
DEPTH_SCALE = 0.5
# 薄板スプラインの滑らかさ。大きいほど特徴点を厳密には通らない。
TPS_SMOOTHING = 1.0
# オプティカルフローの往復のずれ（画素）がこの値のとき、信頼度を exp(-1) にする。
FLOW_CONSISTENCY = 2.0
# オプティカルフローの補正がこれより大きい（画素）ところは信頼しない。
MAX_FLOW = 40.0
# 大きさと傾きの補正の回転がこれを超えたら、特徴点の検出が外れたとみなし、回転なしで合わせ直す。拡大率はこの範囲に収める。
MAX_NORMALIZE_ROTATION = 30.0
NORMALIZE_SCALE_RANGE = (0.5, 2.0)


@dataclass
class Correspondence:
    """正面の各画素が、向きを変えた絵（大きさと傾きをそろえたもの）のどこに当たるか。"""

    target_x: np.ndarray
    target_y: np.ndarray
    confidence: np.ndarray
    tps: RBFInterpolator
    normalize: np.ndarray
    normalized_landmarks: dict[str, tuple[float, float]]


def head_frame(points: np.ndarray) -> tuple[np.ndarray, float]:
    """特徴点から、頭の中心と半径を決める。"""
    center = points.mean(axis=0)
    return center, HEAD_RADIUS_SCALE * float(np.sqrt(((points - center) ** 2).sum(axis=1)).max())


def rotate_rigid(points: np.ndarray, center: np.ndarray, radius: float, yaw: float, pitch: float) -> np.ndarray:
    """点を、中心 center・半径 radius の楕円体の表面にあるとみなし、yaw → pitch の順に回したときの画面上の位置を返す。"""
    rel = points - center
    z = DEPTH_SCALE * np.sqrt(np.clip(radius**2 - (rel**2).sum(axis=1), 0, None))
    return rotate_with_depth(points, center, z, yaw, pitch)


def rotate_with_depth(points: np.ndarray, center: np.ndarray, z: np.ndarray | float, yaw: float, pitch: float) -> np.ndarray:
    """奥行き z（手前が正）の点を、center のまわりに yaw → pitch の順に回したときの画面上の位置を返す（固定テストモデルと同じ式）。"""
    rel = points - center
    z = np.broadcast_to(np.asarray(z, dtype=np.float64), (len(points),))
    cy, sy = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    cp, sp = math.cos(math.radians(pitch)), math.sin(math.radians(pitch))
    x = rel[:, 0] * cy + z * sy
    z1 = -rel[:, 0] * sy + z * cy
    y = rel[:, 1] * cp - z1 * sp
    return np.stack([x, y], axis=1) + center


def fit_similarity(source: np.ndarray, target: np.ndarray, rotate: bool = True, scale_range: tuple[float, float] | None = None) -> np.ndarray:
    """source を target に最小二乗で合わせる相似変換（拡大・回転・平行移動）を 2×3 の行列で返す（Umeyama の方法）。
    rotate が偽なら、拡大率は回転ありで求めた値のまま、回転を 0 にして中心どうしを合わせる。scale_range を渡すと拡大率をその範囲に収める。"""
    mu_s, mu_t = source.mean(axis=0), target.mean(axis=0)
    s, t = source - mu_s, target - mu_t
    u, d, vt = np.linalg.svd(t.T @ s / len(source))
    sign = np.diag([1.0, np.sign(np.linalg.det(u @ vt))])
    rotation = u @ sign @ vt if rotate else np.eye(2)
    scale = float(np.trace(np.diag(d) @ sign) / (s**2).sum(axis=1).mean())
    if scale_range is not None:
        scale = min(max(scale, scale_range[0]), scale_range[1])
    matrix = np.zeros((2, 3))
    matrix[:, :2] = scale * rotation
    matrix[:, 2] = mu_t - scale * rotation @ mu_s
    return matrix


def apply_affine(matrix: np.ndarray, points: np.ndarray) -> np.ndarray:
    """2×3 の行列で点を変換する。"""
    return points @ matrix[:, :2].T + matrix[:, 2]


def normalizing_transform(
    front: dict[str, tuple[float, float]], view: dict[str, tuple[float, float]], yaw: float, pitch: float, stable: set[str] | None = None
) -> np.ndarray:
    """向きを変えた絵の特徴点を、正面の特徴点を硬い頭として回した位置に合わせる相似変換を返す。画像編集モデルが変えた大きさと傾きを取り除く。
    stable を渡すと、その特徴点だけで合わせる。回転が大きすぎるときは検出が外れたとみなして回転なしで合わせ、拡大率は範囲に収める。"""
    ids = sorted(set(front) & set(view) & (stable if stable is not None else set(front)))
    front_points = np.array([front[i] for i in ids], dtype=np.float64)
    view_points = np.array([view[i] for i in ids], dtype=np.float64)
    center, radius = head_frame(front_points)
    predicted = rotate_rigid(front_points, center, radius, yaw, pitch)
    matrix = fit_similarity(view_points, predicted, scale_range=NORMALIZE_SCALE_RANGE)
    if abs(math.degrees(math.atan2(matrix[1, 0], matrix[0, 0]))) > MAX_NORMALIZE_ROTATION:
        matrix = fit_similarity(view_points, predicted, rotate=False, scale_range=NORMALIZE_SCALE_RANGE)
    return matrix


def correspond(
    front_image: np.ndarray,
    view_image: np.ndarray,
    front_landmarks: dict[str, tuple[float, float]],
    view_landmarks: dict[str, tuple[float, float]],
    yaw: float,
    pitch: float,
    region: np.ndarray,
    stable: set[str] | None = None,
) -> Correspondence:
    """region（正面の頭の範囲）の各画素について、向きを変えた絵の対応する位置と信頼度を求める。stable は大きさと傾きの補正と、薄板スプラインに使う特徴点。"""
    height, width = front_image.shape[:2]
    normalize = normalizing_transform(front_landmarks, view_landmarks, yaw, pitch, stable)
    normalized = cv2.warpAffine(view_image, normalize, (width, height), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)
    ids = sorted(set(front_landmarks) & set(view_landmarks))
    moved = apply_affine(normalize, np.array([view_landmarks[i] for i in ids], dtype=np.float64))
    normalized_landmarks = {i: (float(p[0]), float(p[1])) for i, p in zip(ids, moved)}
    # 薄板スプラインも stable の特徴点だけで作る。輪郭の特徴点は、横顔に近い絵で外れやすく、あごの輪郭を折り曲げる。
    used = [k for k, i in enumerate(ids) if stable is None or i in stable]
    tps = RBFInterpolator(
        np.array([front_landmarks[ids[k]] for k in used], dtype=np.float64), moved[used], kernel="thin_plate_spline", smoothing=TPS_SMOOTHING
    )

    grid_y, grid_x = np.mgrid[0:height, 0:width].astype(np.float32)
    coarse = tps(np.stack([grid_x.ravel(), grid_y.ravel()], axis=1)).astype(np.float32)
    map_x, map_y = coarse[:, 0].reshape(height, width), coarse[:, 1].reshape(height, width)
    warped = cv2.remap(normalized, map_x, map_y, cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)

    front_gray = cv2.cvtColor(front_image[..., :3], cv2.COLOR_RGB2GRAY)
    warped_gray = cv2.cvtColor(warped[..., :3], cv2.COLOR_RGB2GRAY)
    dis = cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_MEDIUM)
    forward = dis.calc(front_gray, warped_gray, None)
    backward = dis.calc(warped_gray, front_gray, None)

    fx, fy = grid_x + forward[..., 0], grid_y + forward[..., 1]
    back_at = cv2.remap(backward, fx, fy, cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)
    mismatch = np.hypot(forward[..., 0] + back_at[..., 0], forward[..., 1] + back_at[..., 1])
    confidence = np.exp(-((mismatch / FLOW_CONSISTENCY) ** 2))
    confidence[np.hypot(forward[..., 0], forward[..., 1]) > MAX_FLOW] = 0
    confidence[~region] = 0

    target_x = cv2.remap(map_x, fx, fy, cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)
    target_y = cv2.remap(map_y, fx, fy, cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)
    return Correspondence(target_x, target_y, confidence.astype(np.float32), tps, normalize, normalized_landmarks)
