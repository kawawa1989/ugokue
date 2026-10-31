"""顔の向きと表情の差分の、生成の設定とまとめのテスト。"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import pytest
from PIL import Image

from ugokue_pipeline.views import FRONT, contact_sheet, load_spec, view_name, write_views

SAMPLES = Path(__file__).resolve().parents[2] / "samples" / "views.json"


def test_view_names_encode_angles() -> None:
    assert view_name(30, -20) == "x+30_y-20"
    assert view_name(0, 0) == FRONT == "x+00_y+00"


def test_sample_spec_covers_the_angle_grid_and_expressions() -> None:
    spec = load_spec(SAMPLES)

    angles = {(v.angle_x, v.angle_y) for v in spec.views if v.expression is None}
    assert angles == {(x, y) for x in (-30, 0, 30) for y in (-20, 0, 20)} - {(0, 0)}
    assert [v.name for v in spec.views if v.expression is not None] == ["eyes-closed", "mouth-open"]
    assert len({v.name for v in spec.views}) == len(spec.views)
    assert spec.model == "Qwen/Qwen-Image-Edit-2511"


def test_prompts_expand_the_keep_sentence() -> None:
    spec = load_spec(SAMPLES)

    for view in spec.views:
        assert "{" not in view.prompt
        assert "exactly the same" in view.prompt


def test_rejects_duplicate_views(tmp_path: Path) -> None:
    path = tmp_path / "views.json"
    data = {"model": "m", "steps": 1, "trueCfgScale": 1, "negativePrompt": " ", "seed": 0, "keep": "",
            "views": [{"angleX": 30, "angleY": 0, "prompt": "a"}, {"angleX": 30, "angleY": 0, "prompt": "b"}]}
    path.write_text(json.dumps(data), encoding="utf-8")

    with pytest.raises(ValueError):
        load_spec(path)


def test_write_views_pastes_heads_and_judges_poses(tmp_path: Path) -> None:
    source = np.zeros((20, 20, 3), dtype=np.uint8)
    raw = {"x+30_y+00": np.full((20, 20, 3), 100, dtype=np.uint8)}
    mask = np.zeros((20, 20), dtype=np.float32)
    mask[:10] = 1
    front = _keypoints(0)
    landmarks = {FRONT: [front], "x+30_y+00": [_keypoints(2)]}

    summary = write_views(tmp_path, source, raw, mask, landmarks, {"seed": 0}, {"x+30_y+00": (30, 0)})

    pasted = np.array(Image.open(tmp_path / "views" / "x+30_y+00.png"))
    assert np.all(pasted[:10] == 100) and np.all(pasted[10:] == 0)
    assert (tmp_path / "raw" / "x+30_y+00.png").exists()
    assert (tmp_path / "views" / f"{FRONT}.png").exists()
    entry = summary["views"]["x+30_y+00"]
    assert entry["outsideHead"]["mismatchRatio"] == 1.0
    assert entry["pose"]["yawOk"] is True
    assert entry["pose"]["pitchOk"] is None
    assert json.loads((tmp_path / "views.json").read_text(encoding="utf-8"))["run"] == {"seed": 0}


def _keypoints(shift: float) -> dict:
    """正面の顔の28点に、目・鼻・口だけを右へ shift 画素ずらした検出結果を作る。"""
    points = [[0, 10], [5, 18], [10, 20], [15, 18], [20, 10]]
    points += [[4 + i, 6] for i in range(3)] + [[13 + i, 6] for i in range(3)]
    points += [[5 + i, 9] for i in range(3)] + [[5 + i, 11] for i in range(3)]
    points += [[13 + i, 9] for i in range(3)] + [[13 + i, 11] for i in range(3)]
    points += [[10, 13]] + [[8, 16], [10, 15], [12, 16], [10, 17]]
    moved = [[x + (shift if 5 <= i else 0), y, 0.9] for i, (x, y) in enumerate(points)]
    return {"bbox": [0, 0, 20, 20, 0.99], "keypoints": moved}


def test_contact_sheet_lays_out_the_grid_and_expressions(tmp_path: Path) -> None:
    source = np.zeros((20, 10, 3), dtype=np.uint8)
    raw = {"x+30_y+00": source + 50, "eyes-closed": source + 90}
    write_views(tmp_path, source, raw, np.ones((20, 10), dtype=np.float32), {}, {}, {"x+30_y+00": (30, 0)})

    contact_sheet(tmp_path, ["eyes-closed"], width=10)

    sheet = np.array(Image.open(tmp_path / "sheet.png"))
    assert sheet.shape == (80, 30, 3)
    assert tuple(sheet[25, 25]) == (50, 50, 50)  # 2段目の右（x+30_y+00）
    assert tuple(sheet[65, 5]) == (90, 90, 90)  # 4段目の左（eyes-closed）
