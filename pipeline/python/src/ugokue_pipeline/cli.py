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


def _keyforms(args: argparse.Namespace) -> int:
    from .keyforms import build

    names = args.names.split(",") if args.names else sorted(p.name for p in Path(args.views).iterdir() if (p / "landmarks.json").exists())
    summaries = {}
    for name in names:
        summaries[name] = build(Path(args.decomposition) / name / "result", Path(args.views) / name, Path(args.out) / name)
        print(f"wrote {Path(args.out) / name}")
    Path(args.out, "summary.json").write_text(json.dumps(summaries, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return 0 if all(not s["validationIssues"] for s in summaries.values()) else 1


def _names(args: argparse.Namespace) -> list[str]:
    return args.names.split(",") if args.names else sorted(p.name for p in Path(args.keyforms).iterdir() if (p / "model" / "model.json").exists())


def _review(args: argparse.Namespace) -> int:
    from .review import review_pack

    for name in _names(args):
        references = Path(args.references) / name
        review_pack(Path(args.keyforms) / name, references if references.is_dir() else None)
        print(f"wrote {Path(args.keyforms) / name / 'review'}")
    return 0


def _check_review(args: argparse.Namespace) -> int:
    from .review import validate_review

    issues = validate_review(json.loads(Path(args.path).read_text(encoding="utf-8")))
    for issue in issues:
        print(issue)
    return 1 if issues else 0


def main(argv: list[str] | None = None) -> int:
    """コマンドを実行し、成功なら 0 を返す。"""
    parser = argparse.ArgumentParser(prog="ugokue-pipeline")
    sub = parser.add_subparsers(dest="command", required=True)
    report = sub.add_parser("report", help="<directory>/<name>/result を変換・検証・比較して summary.json を書く")
    report.add_argument("directory")
    report.set_defaults(func=_report)
    keyforms = sub.add_parser("keyforms", help="分解結果と正面の特徴点から、頭の3D形状を回して顔の向きのキーフォームを持つモデルを作って評価する")
    keyforms.add_argument("--decomposition", default="artifacts/decomposition")
    keyforms.add_argument("--views", default="artifacts/views")
    keyforms.add_argument("--out", default="artifacts/keyforms")
    keyforms.add_argument("--names", default="", help="カンマ区切りのイラスト名。省略時は --views にあるすべて")
    keyforms.set_defaults(func=_keyforms)
    review = sub.add_parser("review", help="顔の向きの合否をエージェントが判定するための資料（並べた画像と数値）を <keyforms>/<name>/review に書く")
    review.add_argument("--keyforms", default="artifacts/keyforms")
    review.add_argument("--references", default="artifacts/views-45", help="±45° の参考の絵（<references>/<name>/views と landmarks.json）")
    review.add_argument("--names", default="", help="カンマ区切りのイラスト名。省略時は --keyforms にあるすべて")
    review.set_defaults(func=_review)
    check = sub.add_parser("check-review", help="エージェントが書いた判定の結果（review.json）の形と整合を確かめる")
    check.add_argument("path")
    check.set_defaults(func=_check_review)
    args = parser.parse_args(argv)
    return args.func(args)
