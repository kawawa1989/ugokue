# 設計判断の記録（ADR）

設計判断と、その理由を1件1ファイルで残す。

- ファイル名は `NNNN-<英語の短い説明>.md`（番号は連番）
- 書式は [template.md](template.md) に従う
- 決定を変えるときは、既存のファイルを書き換えず新しい ADR を追加し、古い ADR の状態を「置き換え済み（NNNN）」にする

| 番号 | タイトル | 状態 |
| --- | --- | --- |
| [0001](0001-unity-full-scratch-runtime.md) | 実行環境は Unity の自前ランタイムにする | 採用 |
| [0002](0002-languages-python-and-csharp.md) | 開発言語は Python と C# にする | 採用 |
| [0003](0003-tracking-via-nizima-live-relay.md) | トラッキングは nizima LIVE を中継して受け取る | 採用 |
| [0004](0004-intermediate-format-v0.md) | 中間形式 v0 の構造 | 採用 |
| [0005](0005-seamless-view-switching.md) | ビューの切り替えは境界で見た目が一致するときだけ使う | 採用 |
| [0006](0006-tracking-adapter-pipeline.md) | 入力アダプターは、顔を見失ったら値を保持し、ゆっくり戻す | 採用 |
| [0007](0007-decomposition-runner-and-backends.md) | パーツ分解は、実行環境と変換を分け、まず Modal で Python から動かす | 採用 |
