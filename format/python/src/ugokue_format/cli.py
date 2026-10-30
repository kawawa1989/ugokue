"""ugokue-format コマンド（検証、サンプルと固定テストモデルの生成、比較）。"""

from __future__ import annotations

import argparse
import sys

from .compare import compare_dirs
from .face_model import make_test_face
from .package import FormatError
from .sample import make_sample
from .validate import validate_dir

MAX_DIFFS = 20


def _validate(args: argparse.Namespace) -> int:
    """フォルダを検証し、問題があれば表示して 1 を返す。"""
    issues = validate_dir(args.dir)
    for issue in issues:
        print(issue)
    if issues:
        print(f"{len(issues)} 件の問題")
        return 1
    print("OK")
    return 0


def _make_sample(args: argparse.Namespace) -> int:
    """サンプルモデルを書き出す。"""
    make_sample(args.dir)
    return 0


def _make_test_face(args: argparse.Namespace) -> int:
    """固定テストモデルを書き出す。"""
    make_test_face(args.dir)
    return 0


def _compare(args: argparse.Namespace) -> int:
    """2つのフォルダを比べ、違いがあれば先頭から表示して 1 を返す。"""
    try:
        diffs = compare_dirs(args.dir_a, args.dir_b)
    except (OSError, ValueError, FormatError) as e:
        print(f"比較できない: {e}")
        return 1
    for d in diffs[:MAX_DIFFS]:
        print(d)
    if diffs:
        print(f"{len(diffs)} 件の違い")
        return 1
    print("OK")
    return 0


def main(argv: list[str] | None = None) -> int:
    """コマンドライン引数を解釈して、サブコマンドを実行する。"""
    parser = argparse.ArgumentParser(prog="ugokue-format")
    sub = parser.add_subparsers(required=True)
    p = sub.add_parser("validate", help="モデルのフォルダを検証する")
    p.add_argument("dir")
    p.set_defaults(func=_validate)
    p = sub.add_parser("make-sample", help="サンプルモデルを書き出す")
    p.add_argument("dir")
    p.set_defaults(func=_make_sample)
    p = sub.add_parser("make-test-face", help="固定テストモデルを書き出す")
    p.add_argument("dir")
    p.set_defaults(func=_make_test_face)
    p = sub.add_parser("compare", help="2つのモデルのフォルダが同じ内容かを比べる")
    p.add_argument("dir_a")
    p.add_argument("dir_b")
    p.set_defaults(func=_compare)
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
