"""C# の参照レンダラー（ugokue-format-cs render）で、モデルを指定したパラメータの値で描く。"""

from __future__ import annotations

import shutil
import subprocess
from functools import cache
from pathlib import Path

import numpy as np
from PIL import Image

REPO_ROOT = Path(__file__).resolve().parents[4]
CLI_PROJECT = REPO_ROOT / "dotnet" / "Ugokue.Format.Cli"


def available() -> bool:
    """dotnet と C# のコマンドラインツールのプロジェクトがあるか。"""
    return shutil.which("dotnet") is not None and (CLI_PROJECT / "Ugokue.Format.Cli.csproj").is_file()


@cache
def _build() -> None:
    subprocess.run(["dotnet", "build", "-v", "q", "--nologo", str(CLI_PROJECT)], check=True, capture_output=True)


def render(model_dir: str | Path, out_path: str | Path, values: dict[str, float]) -> np.ndarray:
    """モデルを描いた白い背景の RGB 画像を返し、out_path に PNG で書き出す。"""
    _build()
    arguments = [f"{name}={value}" for name, value in values.items()]
    subprocess.run(
        ["dotnet", "run", "--no-build", "--project", str(CLI_PROJECT), "--", "render", str(model_dir), str(out_path), *arguments],
        check=True,
        capture_output=True,
    )
    return np.array(Image.open(out_path).convert("RGB"))
