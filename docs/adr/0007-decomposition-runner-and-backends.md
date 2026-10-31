# 0007 パーツ分解は、実行環境と変換を分け、まず Modal で Python から動かす

- 状態：採用
- 日付：2026-10-31

## 背景

PoC-08 では、イラスト1枚を See-through で層に分け、中間形式 v0 のパーツとして書き出す。See-through は CUDA と VRAM 12〜16GB の GPU を必要とし、開発に使っている Mac（Apple Silicon）では動かない。

チケットには「クラウド GPU 上に ComfyUI ＋ Python の実行環境を作る」とある。一方で、将来は利用者の手元の Windows PC（VRAM 16GB の GPU）で、ComfyUI から生成する使い方も考えている。

## 決定

- パイプラインを2つに分ける。
  - **実行（バックエンド）**：See-through を動かし、決まった形の「分解結果」（層ごとの PNG、深度、実行の記録）を書き出す。
  - **変換**：分解結果を読み、左右の分割などの後処理をして、中間形式 v0 のパーツとして書き出す。GPU を使わず、どの OS でも動く。
- PoC-08 のバックエンドは Modal にし、See-through の推論スクリプトを Python から直接呼ぶ。ComfyUI は PoC-08 では使わない。
- 分解結果の形を、バックエンドに依存しない仕様として決める（[パーツ分解](../spec/part-decomposition.md)）。あとで手元の Windows や ComfyUI（ComfyUI-See-through のノード）のバックエンドを足すときは、この形を書き出すだけでよい。
- Modal でも、VRAM 16GB に収まる設定（bf16、解像度 1280）で動かし、使った VRAM の最大値を記録する。16GB の GPU で同じ結果が出せるかの判断に使う。
- 乱数のシードと、See-through のリポジトリと重みの版を、分解結果の記録に残す。

## 理由

- 変換を分けておけば、GPU がなくても変換のテストを CI で回せる。分解結果を固定のテスト用データとして使える。
- Modal は実行環境（CUDA、Python、See-through の版）をコードで書けるので、同じ環境を作り直せる。使っていないあいだは費用がかからない。
- ComfyUI は画面で試すのには向くが、テストから自動で実行して結果を比べるには、Python から直接呼ぶほうが単純。See-through の中身は同じなので、ComfyUI を使っても分解結果は同じ形にできる。
- 手元の Windows と ComfyUI のルートを先に作らないのは、PoC-08 で確かめたいことが「See-through の分解が使えるか」であり、実行場所ではないため。

## 影響

- 変換のコードは、OS に依存しない書き方（`pathlib`、PNG と JSON だけを読む）にする。
- 手元の Windows で動かす場合、See-through の任意の依存（detectron2 など）が Windows で入るかは未確認。そのバックエンドを作るときに確かめる。
- PoC-08 の実機の結果では、既定の設定（bf16、解像度 1280）は VRAM 15 GiB に絞ると足りず、See-through の group offload を使うと 11 GiB 以下に収まった。手元の 16GB の GPU のバックエンドでは group offload を使う。
- テストイラストは、See-through の元になっている Animagine XL 4.0 で、シードを固定して Modal 上で生成する。
- 利用するモデルのライセンス（See-through のコードは Apache-2.0、重みは CreativeML Open RAIL++-M を引き継ぐ。Animagine XL 4.0 も Open RAIL++-M）は、[パーツ分解](../spec/part-decomposition.md) に一覧にする。
