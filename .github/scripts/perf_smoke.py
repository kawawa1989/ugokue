"""ビルドしたプレイヤーを計測モードで起動し、保存された計測 CSV を検証する。"""

import argparse
import csv
import subprocess
import sys
from pathlib import Path


def run_player(executable: Path, output: Path, seconds: float, timeout: float) -> None:
    """プレイヤーを計測引数つきで起動し、終了まで待つ。"""
    command = [
        str(executable),
        "-batchmode",
        "-nographics",
        "-logFile",
        "-",
        "-ugokue-record-seconds",
        str(seconds),
        "-ugokue-record-out",
        str(output),
        "-ugokue-quit-after-record",
    ]
    subprocess.run(command, check=True, timeout=timeout)


def verify_samples(path: Path, seconds: float) -> int:
    """フレーム番号が連続し、指定秒数を覆っていることを確かめ、フレーム数を返す。"""
    with path.open(newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        if reader.fieldnames != ["frame", "timestamp_s", "frame_time_ms", "cpu_time_ms"]:
            raise ValueError(f"unexpected header: {reader.fieldnames}")
        rows = list(reader)

    if not rows:
        raise ValueError("no frames recorded")

    previous = -1.0
    for expected, row in enumerate(rows):
        if int(row["frame"]) != expected:
            raise ValueError(f"frame index gap at row {expected}: {row['frame']}")
        timestamp = float(row["timestamp_s"])
        if timestamp < previous:
            raise ValueError(f"timestamp decreased at frame {expected}")
        if float(row["frame_time_ms"]) < 0:
            raise ValueError(f"negative frame time at frame {expected}")
        previous = timestamp

    if float(rows[0]["timestamp_s"]) != 0:
        raise ValueError("first timestamp is not 0")
    if previous < seconds:
        raise ValueError(f"recording covers {previous}s, expected at least {seconds}s")
    return len(rows)


def verify_summary(path: Path, frame_count: int, seconds: float) -> None:
    """集計 CSV のフレーム数と記録時間が本体 CSV と一致することを確かめる。"""
    with path.open(newline="", encoding="utf-8") as f:
        metrics = {row["metric"]: row["value"] for row in csv.DictReader(f)}

    if int(metrics["frame_count"]) != frame_count:
        raise ValueError(f"summary frame_count {metrics['frame_count']} != {frame_count}")
    if float(metrics["duration_s"]) < seconds:
        raise ValueError(f"summary duration_s {metrics['duration_s']} < {seconds}")


def main() -> int:
    """コマンドライン引数を読み、起動と検証を行う。"""
    parser = argparse.ArgumentParser()
    parser.add_argument("executable", type=Path)
    parser.add_argument("--out-dir", type=Path, required=True)
    parser.add_argument("--seconds", type=float, default=3)
    parser.add_argument("--timeout", type=float, default=180)
    args = parser.parse_args()

    output = (args.out_dir / "perf.csv").resolve()
    run_player(args.executable, output, args.seconds, args.timeout)

    frame_count = verify_samples(output, args.seconds)
    verify_summary(output.with_name("perf_summary.csv"), frame_count, args.seconds)
    print(f"OK: {frame_count} contiguous frames covering {args.seconds}s")
    print(output.with_name("perf_summary.csv").read_text(encoding="utf-8"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
