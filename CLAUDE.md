# ugokue

イラスト1枚から、配信で使える高可動域の VTuber アバターを半自動で作るツール。
コーディングは AI が行い、人はチケットの確認項目を見てマージを判断する。

## 人とのやり取り

- チャットでの返答は、常に日本語で書く。コマンド、コード、ファイル名、識別子はそのまま書いてよい。

## 情報の置き場所

| 内容 | 置き場所 | 扱い |
| --- | --- | --- |
| 何を作るか・なぜ作るか（ロードマップ、フェーズ、マイルストーン、チケット） | GitHub Issue | 正本は [#20 実装ロードマップ](https://github.com/kawawa1989/ugokue/issues/20)。その sub-issue としてフェーズ → マイルストーン → 実装チケットがつながる |
| すぐには対応しないが、あとで対応するチケット | GitHub Issue | [#26 Backlog](https://github.com/kawawa1989/ugokue/issues/26) の sub-issue にする |
| コードが厳密に従う技術仕様（データ形式、座標系、単位、守るべき条件、クラス関係図、シーケンス図） | `docs/spec/` | コードと同じ PR で更新する |
| 設計判断とその理由 | `docs/adr/` | 1件1ファイル。決定を変えるときは新しい ADR を追加する |
| 変更の経緯 | コミットメッセージと PR の説明 | コードのコメントには書かない |

## 作業の進め方

1. 作業前に `gh issue view 20` と、担当チケットの Issue を読む。チケットの受け入れ条件とテスト項目がそのまま作業の仕様になる。
2. `docs/spec/` と `docs/adr/` の関係するページを読む。Issue と `docs/spec/` が食い違う場合は、作業を止めて人に確認する。
3. Issue ごとにブランチを作る。ブランチ名は `<issue番号>-<短い英語の説明>`（例：`3-dev-env-setup`）。
4. 受け入れ条件をテストに落としてから実装する。
5. チケットのテスト項目は、実装と同じ PR で GitHub Actions に追加する。テストを追加・変更・削除したら、同じ PR で [CI のテスト](docs/spec/ci.md) の一覧とチケットとの対応表を更新する。
6. 構造を変えたら、同じ PR で `docs/spec/` の図と説明を更新する。
7. PR を作り、本文に `Closes #<issue番号>` を書く。チケットの確認項目（人の確認）を PR テンプレートの欄に転記し、判断材料（スクリーンショット、角度スイープ動画、計測値）を添付する。

### バックログ

作業中に見つかった、担当チケットの範囲外の課題や、すぐには対応しないと決めた改善は、チケットとして起票して [#26 Backlog](https://github.com/kawawa1989/ugokue/issues/26) の sub-issue にする。

- 本文は実装チケットと同じ形（概要、やること、受け入れ条件、テスト項目、確認項目）で書く。冒頭の「マイルストーン」は「バックログ（対応する時期）」にし、見つかった経緯（PR、ADR）を書く。
- 起票するかどうかは人と相談して決める。起票したら、PR の説明か会話で Issue 番号を伝える。
- バックログのチケットに着手するときは、人が対応するマイルストーンへ移してから、通常のチケットと同じ手順で進める。

## 禁止事項

- main への直接 push、強制 push、ブランチの削除
- PR のマージ（`gh pr merge` を含む）。マージは人が行う
- テストを通すためにテストを削除・弱めること
- Issue 本文の書き換え（コメントでの報告は可）

## 開発言語

- AI 生成パイプライン：Python（`pipeline/`）
- Unity ランタイム、編集画面、nizima LIVE 連携：C#（`unity/`）
- 中間形式の読み書きは Python と C# の両方で実装し、往復テストで一致を確かめる
- Rust など他の言語は使わない（[ADR 0002](docs/adr/0002-languages-python-and-csharp.md)）

## コードコメントのルール

書いてよいコメントは次の2種類だけ。

- クラスの説明：そのクラスが提供する機能
- 関数の説明：その関数が提供する機能

書き方

- Python は docstring、C# は `/// <summary>` で書く
- 1〜2文で、機能だけを事実として書く
- 引数・戻り値の説明は、名前と型で伝わらない場合だけ書く

書かないこと

- 処理の説明や、コードを言い換えただけのコメント
- 過去の変更や経緯（「以前は〜だった」「〜から変更」「〜を修正」）
- 意思決定の理由（ADR に書く）
- 主観的・強調的な表現（「重要」「危険」「これが効く」「注意」）
- 絵文字、TODO、FIXME（やり残しは Issue にする）
- コメントアウトしたコード

守るべき条件や数値の根拠など、コードから読み取れない事柄は `docs/spec/` の該当ページに書く。

```python
# よい例
class KeyformInterpolator:
    """パラメータの値から、各頂点の変形後の位置を求める。"""

# 悪い例
class KeyformInterpolator:
    """重要！以前の線形補間では破綻したため双線形に変更"""
```

## ディレクトリ構成

```
pipeline/   Python（AI 生成パイプライン。GPU を使う処理は Modal で実行する）
unity/      C#（Unity プロジェクト）
dotnet/     Unity に依存しない C# を .NET でビルド・テストするためのソリューション
format/     中間形式のスキーマ、サンプル、検証ツール
docs/spec/  技術仕様
docs/adr/   設計判断の記録
```

各ディレクトリは、それを作るチケットで追加する。ビルド・テスト・lint のコマンドは、追加したチケットでこのファイルに追記する。

## ビルドとテスト

Unity のバージョンは `unity/ProjectSettings/ProjectVersion.txt` に従う（6000.0.28f1）。以下はリポジトリのルートで実行する。macOS の Unity は `/Applications/Unity/Hub/Editor/6000.0.28f1/Unity.app/Contents/MacOS/Unity` にある（以下 `$UNITY`）。

| 目的 | コマンド |
| --- | --- |
| Unity に依存しないコードのテスト | `dotnet test dotnet/Ugokue.sln` |
| Unity の EditMode テスト | `$UNITY -batchmode -nographics -projectPath unity -runTests -testPlatform EditMode -testResults <出力.xml> -logFile <ログ>` |
| macOS ビルド | `$UNITY -batchmode -nographics -quit -projectPath unity -executeMethod Ugokue.Editor.BuildCommand.BuildMacOS -ugokue-build-out <出力.app> -logFile <ログ>` |
| Windows ビルド | 上の `BuildMacOS` を `BuildWindows` に、出力を `<出力.exe>` にする |
| Main シーンの作り直し | `$UNITY -batchmode -nographics -quit -projectPath unity -executeMethod Ugokue.Editor.ProjectBootstrap.CreateMainScene -logFile <ログ>` |
| 計測の記録 | `<プレイヤー> -ugokue-record-seconds 30 -ugokue-record-out <出力.csv> -ugokue-quit-after-record`（引数は [性能の計測](docs/spec/performance-measurement.md)） |
| 中間形式の検証 | `uv run --project format/python ugokue-format validate <モデルのフォルダ>` |
| 中間形式の Python テスト（C# との往復を含む） | `uv run --project format/python pytest format/python` |
| サンプルモデルの生成 | `uv run --project format/python ugokue-format make-sample format/samples/minimal` |
| 固定テストモデルの生成 | `uv run --project format/python ugokue-format make-test-face format/samples/test-face` |
| 画像回帰テストの基準画像の作り直し | `UGOKUE_UPDATE_BASELINES=1 dotnet test dotnet/Ugokue.sln`（[実行時の変形と描画](docs/spec/runtime-deformation.md#画像回帰テスト)） |
| nizima LIVE の受信の実機確認 | `dotnet run --project dotnet/Ugokue.Live.Probe -- --seconds 60`（[nizima LIVE からの受信](docs/spec/nizima-live.md#実機での確認)） |
| 記録したトラッキング値で動かす | `<プレイヤー> -ugokue-tracking-playback <messages.jsonl>`（[入力アダプター](docs/spec/tracking-adapter.md#記録の再生)） |
| 生成パイプラインのテスト（GPU 不要） | `uv run --project pipeline/python pytest pipeline/python` |
| パーツ分解（Modal の GPU） | `uv run --project pipeline/python --extra modal modal run pipeline/python/src/ugokue_pipeline/modal_app.py::decompose_images`（[パーツ分解](docs/spec/part-decomposition.md#実行のしかた)） |
| 向きと表情の差分の生成（Modal の GPU） | `uv run --project pipeline/python --extra modal modal run pipeline/python/src/ugokue_pipeline/modal_app.py::generate_views`（[向きと表情の差分の生成](docs/spec/view-generation.md#実行のしかた)） |
| 計測の起動確認 | `python3 .github/scripts/perf_smoke.py <プレイヤーの実行ファイル> --out-dir <出力先>` |

- 中間形式の項目を変えるときは、`docs/spec/intermediate-format.md`、`format/schema/model.schema.json`、Python と C# の両方の実装、サンプルを同じ PR で更新する。
- `format/samples/` のフォルダは生成コマンドの出力なので、直接編集しない。生成コードを変えて再生成する（[固定テストモデル](docs/spec/test-model.md)）。
- Unity を参照しないコードは `unity/Packages/com.ugokue.core/Runtime/` に置く。asmdef の `noEngineReferences` を true に保ち、`dotnet/Ugokue.Core.Tests/` に xUnit のテストを書く。
- Unity を参照するコードは `unity/Assets/Ugokue/` に置き、テストは `unity/Assets/Ugokue/Tests/EditMode/` に書く。
- Unity のテストを実行するときは `-quit` を付けない。付けるとテストを実行せずに終了し、結果ファイルも作られない。
- `-logFile` などの出力を `unity/` の外の相対パス（`../` で始まるもの）にしない。Unity が警告を出す。
- CI（`.github/workflows/ci.yml`）のジョブとテストの一覧は [CI のテスト](docs/spec/ci.md) にある。Unity のジョブはリポジトリの Secrets `UNITY_LICENSE`、`UNITY_EMAIL`、`UNITY_PASSWORD` を使う。
