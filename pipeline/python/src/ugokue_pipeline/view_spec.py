"""顔の向きと表情の差分の、生成の設定（指示文と乱数）。GPU のコンテナでも読めるよう、標準ライブラリだけを使う。"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any


def view_name(angle_x: int, angle_y: int) -> str:
    """向きの絵の名前（例：x+30_y-20）。"""
    return f"x{angle_x:+03d}_y{angle_y:+03d}"


# 正面の絵。生成せず、元のイラストをそのまま使う。
FRONT = view_name(0, 0)


@dataclass(frozen=True)
class View:
    """生成する絵1枚。向きの絵は angle_x、angle_y を、表情の絵は expression を持つ。"""

    name: str
    prompt: str
    angle_x: int = 0
    angle_y: int = 0
    expression: str | None = None


@dataclass
class ViewSpec:
    """生成の設定。"""

    model: str
    steps: int
    true_cfg_scale: float
    negative_prompt: str
    seed: int
    views: list[View]
    raw: dict[str, Any] = field(default_factory=dict)


def load_spec(path: str | Path) -> ViewSpec:
    """生成の設定（views.json）を読む。"""
    return parse_spec(json.loads(Path(path).read_text(encoding="utf-8")))


def parse_spec(data: dict[str, Any]) -> ViewSpec:
    """生成の設定を読み、指示文の {keep} を置き換える。"""
    views = []
    for entry in data["views"]:
        prompt = entry["prompt"].replace("{keep}", data["keep"]).strip()
        if "expression" in entry:
            views.append(View(entry["expression"], prompt, expression=entry["expression"]))
        else:
            x, y = int(entry["angleX"]), int(entry["angleY"])
            views.append(View(view_name(x, y), prompt, x, y))
    names = [v.name for v in views]
    if len(set(names)) != len(names) or FRONT in names:
        raise ValueError(f"views must be unique and must not include {FRONT}: {names}")
    return ViewSpec(data["model"], int(data["steps"]), float(data["trueCfgScale"]), data["negativePrompt"], int(data["seed"]), views, data)
