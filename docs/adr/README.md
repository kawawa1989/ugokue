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
| [0008](0008-view-generation-with-image-editing.md) | 向きと表情の絵は、頭の周りを切り出して画像編集モデルに指示文で変えさせ、分解結果のマスクで頭だけを戻す | 採用 |
| [0009](0009-keyform-estimation-with-landmarks-flow-and-depth.md) | 顔の向きのキーフォームは、特徴点・オプティカルフロー・層の奥行きから推定する | 置き換え済み（0011） |
| [0010](0010-defer-per-vertex-depth-ordering.md) | 頂点ごとの奥行きによる前後関係の判定は見送り、描画順とビューの切り替えのままにする | 採用 |
| [0011](0011-face-rotation-by-head-3d-shape.md) | 顔の向きのキーフォームは、頭の3D形状を回して投影して作る | 採用 |
| [0012](0012-agent-review-of-face-rotation.md) | 顔の向きの出来は、生成した参考の絵と数値からエージェントが判定し、イラストごとの値を直す | 採用 |
