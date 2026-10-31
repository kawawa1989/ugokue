"""分解結果のまとめと、2回の分解結果の比較のテスト。"""

from __future__ import annotations

import json
import shutil
from pathlib import Path

import numpy as np
from PIL import Image

from ugokue_pipeline.report import compare_results, write_report


def test_report_converts_validates_and_compares_each_result(result_dir: Path, tmp_path: Path) -> None:
    item = tmp_path / "out" / "sample"
    shutil.copytree(result_dir, item / "result")

    summary = write_report(tmp_path / "out", [item])

    entry = summary["results"][0]
    assert entry["name"] == "sample"
    assert entry["validationIssues"] == []
    assert entry["parts"] == 9
    assert entry["comparison"]["meanAbsDiff"] == 0
    assert entry["run"] == {"backend": "test", "seed": 42}
    assert (item / "model" / "model.json").exists()
    assert (item / "composite.png").exists() and (item / "diff.png").exists()
    assert json.loads((tmp_path / "out" / "summary.json").read_text(encoding="utf-8")) == summary


def test_identical_results_have_no_differences(result_dir: Path, tmp_path: Path) -> None:
    copy = tmp_path / "copy"
    shutil.copytree(result_dir, copy)

    summary = compare_results(result_dir, copy)

    assert summary["identical"] is True
    assert summary["maxAbsDiff"] == 0
    assert summary["tagsOnlyInOne"] == []


def test_detects_a_changed_layer(result_dir: Path, tmp_path: Path) -> None:
    copy = tmp_path / "copy"
    shutil.copytree(result_dir, copy)
    path = copy / "layers" / "face.png"
    image = np.array(Image.open(path))
    image[0, 0, 0] -= 7
    Image.fromarray(image).save(path)

    summary = compare_results(result_dir, copy)

    assert summary["identical"] is False
    assert summary["maxAbsDiff"] == 7
    assert summary["layers"]["face"]["differentPixels"] == 1
