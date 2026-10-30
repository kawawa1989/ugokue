# 技術仕様

コードが厳密に従う技術仕様を置く。何を作るか・なぜ作るかは [#20 実装ロードマップ](https://github.com/kawawa1989/ugokue/issues/20) とその sub-issue にある。

## 書くこと

- データ形式（中間形式の JSON スキーマ、ファイル構成、バージョン）
- 座標系、単位、値の範囲
- 守るべき条件と、その数値の根拠
- モジュールの役割、クラス関係図、シーケンス図

関数単位の説明はここに書かず、コードのクラス・関数の説明コメントに書く。

## 書き方

- 図は Mermaid で書く（GitHub 上で図として表示される）
- 1ページ1テーマ。ファイル名は英語の小文字とハイフン（例：`intermediate-format.md`）
- 構造や形式を変える PR では、同じ PR でこのディレクトリを更新する

## ページ一覧

| ページ | 内容 | 追加するチケット |
| --- | --- | --- |
| [ci.md](ci.md) | CI で実行するテストの一覧 | PoC-01 |
| [performance-targets.md](performance-targets.md) | PoC の数値目標（仮） | PoC-01 |
| [performance-measurement.md](performance-measurement.md) | fps・フレーム時間の計測と CSV 形式 | PoC-01 |
| （未作成）`intermediate-format.md` | 中間形式 v0 | PoC-02 |
