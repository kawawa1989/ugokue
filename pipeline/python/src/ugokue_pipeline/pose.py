"""anime-face-detector の28点の特徴点から、顔の向きのおおよその値を求め、指定した向きを向いているかを判定する。"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

import numpy as np

# 28点の番号（anime-face-detector の順）。輪郭は画像の左端・あご・右端の順。
CONTOUR_LEFT = 0
CONTOUR_RIGHT = 4
EYES = range(11, 23)
NOSE = 23
MOUTH = range(24, 28)

# 向きの値の変化がこれより小さければ、向いていないとみなす。
MIN_DELTA = 0.02


@dataclass(frozen=True)
class Pose:
    """顔の向きのおおよその値。yaw は正で画面の右、pitch は正で上を向く（角度ではなく、比べるための値）。"""

    yaw: float
    pitch: float


def estimate_pose(faces: list[dict[str, Any]]) -> Pose | None:
    """検出した顔のうち、いちばん確からしい顔の向きを求める。顔がなければ None。"""
    if not faces:
        return None
    face = max(faces, key=lambda f: f["bbox"][4])
    points = np.asarray(face["keypoints"], dtype=np.float64)[:, :2]
    left, right = points[CONTOUR_LEFT], points[CONTOUR_RIGHT]
    eyes = points[list(EYES)].mean(axis=0)
    mouth = points[list(MOUTH)].mean(axis=0)
    # 目の中心が、顔の輪郭の左右の中央からどれだけ右にあるか（顔の幅に対する割合）。
    yaw = (eyes[0] - (left[0] + right[0]) / 2) / (right[0] - left[0])
    # 鼻が、目と口のあいだのどこにあるか。上を向くと鼻先が目に近づくので、符号を反転する。
    pitch = -(points[NOSE][1] - eyes[1]) / (mouth[1] - eyes[1])
    return Pose(float(yaw), float(pitch))


def judge(front: Pose | None, view: Pose | None, angle_x: float, angle_y: float) -> dict[str, Any]:
    """正面の絵からの向きの変化が、指定した向き（AngleX、AngleY）と同じ向きかを判定する。角度が 0 の軸は判定しない（None）。"""
    if front is None or view is None:
        return {"detected": False, "yawOk": False if angle_x else None, "pitchOk": False if angle_y else None}
    yaw_delta = view.yaw - front.yaw
    pitch_delta = view.pitch - front.pitch
    return {
        "detected": True,
        "yawDelta": round(yaw_delta, 4),
        "pitchDelta": round(pitch_delta, 4),
        "yawOk": _same_direction(yaw_delta, angle_x),
        "pitchOk": _same_direction(pitch_delta, angle_y),
    }


def _same_direction(delta: float, angle: float) -> bool | None:
    if angle == 0:
        return None
    return bool(np.sign(delta) == np.sign(angle) and abs(delta) >= MIN_DELTA)
