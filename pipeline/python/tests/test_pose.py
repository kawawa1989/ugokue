"""顔の特徴点からの向きの推定と判定のテスト。"""

from __future__ import annotations

import pytest

from ugokue_pipeline.pose import estimate_pose, judge


def _face(shift_x: float = 0, nose_y: float = 13) -> dict:
    """28点の検出結果。目・鼻・口を右へ shift_x 画素ずらし、鼻の高さを nose_y にする。"""
    points = [[0, 10], [5, 18], [10, 20], [15, 18], [20, 10]]
    points += [[4 + i, 6] for i in range(3)] + [[13 + i, 6] for i in range(3)]
    points += [[5 + i, 9] for i in range(3)] + [[5 + i, 11] for i in range(3)]
    points += [[13 + i, 9] for i in range(3)] + [[13 + i, 11] for i in range(3)]
    points += [[10, nose_y]] + [[8, 16], [10, 15], [12, 16], [10, 17]]
    moved = [[x + (shift_x if 5 <= i else 0), y, 0.9] for i, (x, y) in enumerate(points)]
    return {"bbox": [0, 0, 20, 20, 0.99], "keypoints": moved}


def test_front_face_has_zero_yaw() -> None:
    pose = estimate_pose([_face()])

    assert pose is not None
    assert pose.yaw == pytest.approx(0)


def test_features_moving_right_mean_facing_right() -> None:
    assert estimate_pose([_face(shift_x=2)]).yaw > 0
    assert estimate_pose([_face(shift_x=-2)]).yaw < 0


def test_nose_closer_to_the_eyes_means_facing_up() -> None:
    front = estimate_pose([_face()])

    assert estimate_pose([_face(nose_y=11)]).pitch > front.pitch
    assert estimate_pose([_face(nose_y=15)]).pitch < front.pitch


def test_uses_the_most_confident_face() -> None:
    weak = _face(shift_x=-3)
    weak["bbox"][4] = 0.5

    assert estimate_pose([weak, _face(shift_x=2)]).yaw > 0
    assert estimate_pose([]) is None


def test_judge_checks_direction_only_on_turned_axes() -> None:
    front = estimate_pose([_face()])
    right_up = estimate_pose([_face(shift_x=2, nose_y=11)])

    result = judge(front, right_up, angle_x=30, angle_y=20)
    assert result["yawOk"] is True and result["pitchOk"] is True

    wrong = judge(front, right_up, angle_x=-30, angle_y=0)
    assert wrong["yawOk"] is False and wrong["pitchOk"] is None

    assert judge(front, None, angle_x=30, angle_y=0) == {"detected": False, "yawOk": False, "pitchOk": None}
