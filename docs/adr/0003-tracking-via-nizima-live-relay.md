# 0003 トラッキングは nizima LIVE を中継して受け取る

- 状態：採用
- 日付：2026-10-27

## 背景

配信者の顔の動きを iPhone でトラッキングし、Unity のアバターに反映する必要がある。nizima LIVE との連携が要件にある。

## 決定

iPhone の nizima LIVE TRACKER → PC 版 nizima LIVE → nizima LIVE Plugin API（WebSocket）→ Unity の経路で、トラッキング値を受け取る。Unity 側に入力アダプターを置き、入力元を差し替えられるようにする。

## 理由

- nizima LIVE TRACKER の公式の接続先は PC 版 nizima LIVE だけで、他のアプリに直接つなぐ方法や通信形式は公開されていない。
- PC 版 nizima LIVE には公式の Plugin API があり、PC 内の WebSocket（既定ポート 22022）で描画ごとのトラッキング値（LiveParameter）を受け取れる。
- PC 版 nizima LIVE は Live2D モデルしか表示できないため、アバターの描画は Unity 側で行う。

## 影響

- PC 版 nizima LIVE にダミーの Live2D モデルを読み込んでおく必要がある可能性が高い（PoC-06 で確認する）。
- 配信画面は Unity アプリの出力を OBS などで取り込む。
- App Store の説明では、TRACKER の商用利用は PC 版 nizima LIVE に接続し、有料プランを使う場合に限られる。利用条件は MVP-16 で確認する。
- nizima LIVE を通さない入力経路（iFacialMocap、自作の ARKit アプリなど）を追加できるよう、入力アダプターで入力元を抽象化する。

## 参考

- [nizima LIVE Plugin API](https://github.com/Live2D/nizimaLIVEPluginAPI)
- [How to Connect with iPhone (nizima LIVE TRACKER)](https://docs.nizimalive.com/en/tutorials/how-to-connect-tracker/)
