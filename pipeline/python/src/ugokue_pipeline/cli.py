"""生成パイプラインのコマンドラインツール。GPU を使う処理は modal_app.py にある。"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from .report import write_report


def _report(args: argparse.Namespace) -> int:
    items = sorted(p for p in Path(args.directory).iterdir() if (p / "result" / "layers.json").exists())
    summary = write_report(Path(args.directory), items)
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0 if all(not x["validationIssues"] for x in summary["results"]) else 1


def main(argv: list[str] | None = None) -> int:
    """コマンドを実行し、成功なら 0 を返す。"""
    parser = argparse.ArgumentParser(prog="ugokue-pipeline")
    sub = parser.add_subparsers(dest="command", required=True)
    report = sub.add_parser("report", help="<directory>/<name>/result を変換・検証・比較して summary.json を書く")
    report.add_argument("directory")
    report.set_defaults(func=_report)
    args = parser.parse_args(argv)
    return args.func(args)
