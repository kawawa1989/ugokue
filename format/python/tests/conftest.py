"""テスト共通のパスとフィクスチャ。"""

from __future__ import annotations

import json
import shutil
from collections.abc import Callable
from pathlib import Path
from typing import Any

import pytest

from ugokue_format.package import dump_model_json
from ugokue_format.validate import Issue, validate_dir

REPO_ROOT = Path(__file__).resolve().parents[3]
SAMPLE_DIR = REPO_ROOT / "format" / "samples" / "minimal"
TEST_FACE_DIR = REPO_ROOT / "format" / "samples" / "test-face"


@pytest.fixture
def sample_copy(tmp_path: Path) -> Path:
    """コミット済みのサンプルを一時フォルダに複製する。"""
    target = tmp_path / "model"
    shutil.copytree(SAMPLE_DIR, target)
    return target


@pytest.fixture
def mutate(sample_copy: Path) -> Callable[[Callable[[dict[str, Any]], object]], list[Issue]]:
    """サンプルの複製の model.json を書き換えて検証し、問題の一覧を返す関数。"""

    def run(change: Callable[[dict[str, Any]], object]) -> list[Issue]:
        path = sample_copy / "model.json"
        doc = json.loads(path.read_text(encoding="utf-8"))
        change(doc)
        path.write_bytes(dump_model_json(doc))
        return validate_dir(sample_copy)

    return run
