"""サンプルの再生成、Python と C# の読み書きの往復のテスト。"""

from __future__ import annotations

import os
import shutil
import subprocess
from pathlib import Path

import pytest
from conftest import REPO_ROOT, SAMPLE_DIR, TEST_FACE_DIR

from ugokue_format.compare import compare_dirs
from ugokue_format.face_model import make_test_face
from ugokue_format.package import FormatError, load, save
from ugokue_format.sample import make_sample

CSHARP_CLI = REPO_ROOT / "dotnet" / "Ugokue.Format.Cli"
SAMPLES = pytest.mark.parametrize("sample", [SAMPLE_DIR, TEST_FACE_DIR], ids=["minimal", "test-face"])


def _files(directory: Path) -> dict[str, bytes]:
    """フォルダ内のすべてのファイルを相対パスとバイト列で返す。"""
    return {p.relative_to(directory).as_posix(): p.read_bytes() for p in directory.rglob("*") if p.is_file()}


def test_sample_regenerates_byte_identically(tmp_path: Path) -> None:
    make_sample(tmp_path / "a")
    make_sample(tmp_path / "b")
    assert _files(tmp_path / "a") == _files(SAMPLE_DIR)
    assert _files(tmp_path / "b") == _files(SAMPLE_DIR)


def test_test_face_regenerates_byte_identically(tmp_path: Path) -> None:
    make_test_face(tmp_path / "a")
    make_test_face(tmp_path / "b")
    assert _files(tmp_path / "a") == _files(TEST_FACE_DIR)
    assert _files(tmp_path / "b") == _files(TEST_FACE_DIR)


@SAMPLES
def test_python_roundtrip(tmp_path: Path, sample: Path) -> None:
    save(load(sample), tmp_path)
    assert compare_dirs(sample, tmp_path) == []
    assert _files(tmp_path) == _files(sample)


def test_load_rejects_unreadable_version(sample_copy: Path) -> None:
    path = sample_copy / "model.json"
    path.write_text(path.read_text(encoding="utf-8").replace('"0.1.0"', '"0.2.0"'), encoding="utf-8")
    with pytest.raises(FormatError, match="0.1.x"):
        load(sample_copy)


def test_compare_reports_differences(sample_copy: Path) -> None:
    path = sample_copy / "model.json"
    path.write_text(path.read_text(encoding="utf-8").replace('"width": 256', '"width": 256.0'), encoding="utf-8")
    assert compare_dirs(SAMPLE_DIR, sample_copy) == []
    path.write_text(path.read_text(encoding="utf-8").replace('"height": 256', '"height": 255'), encoding="utf-8")
    (sample_copy / "textures" / "face.png").write_bytes(b"\x89PNG\r\n\x1a\n")
    assert compare_dirs(SAMPLE_DIR, sample_copy) == ["$.canvas.height: 256 != 255", "textures/face.png: バイト列が違う"]


@SAMPLES
def test_csharp_roundtrip(tmp_path: Path, sample: Path) -> None:
    required = os.environ.get("UGOKUE_REQUIRE_DOTNET") == "1"
    dotnet = shutil.which("dotnet")
    project = CSHARP_CLI / "Ugokue.Format.Cli.csproj"
    if dotnet is None or not project.is_file():
        reason = "dotnet が PATH にない" if dotnet is None else f"{project} がない"
        if required:
            pytest.fail(reason)
        pytest.skip(reason)
    out = tmp_path / "out"
    subprocess.run(
        [dotnet, "run", "--project", str(CSHARP_CLI), "--", "roundtrip", str(sample), str(out)],
        check=True,
        cwd=REPO_ROOT,
    )
    assert compare_dirs(sample, out) == []
