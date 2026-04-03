# Lightning Profiler

Unity EditorのデフォルトCPU Usageモジュールを置き換える、スパイク検出に特化した高機能CPUプロファイラーモジュールです。

## 機能

### スパイク検出
- **Pause on spike** — フレームが閾値を超えた際にPlay Modeを自動停止
- **Log on spike** — スパイクフレームのコールスタック階層をコンソールに出力（Editor・Play Mode両対応）

### フレームフィルタリング
- **Chart filter threshold** — ミリ秒単位の閾値を設定し、それ以下のフレームをチャートから非表示
- **Threshold highlight strip** — 閾値を超えたフレームをチャート下部のバーで視覚的に表示

### 検索マッチング
- **Pause on match** — 検索語に一致するプロファイラーサンプルを含むフレームでPlay Modeを自動停止
- **Search highlight strip** — 検索に一致するフレームをバーで視覚的に表示

### ビュー
- **Timeline view** — スレッド単位のタイムライン表示（カラーコード付き）
- **Hierarchy view** — ソート・スレッド選択対応のサンプル階層表示

## 要件

- Unity 2022.3 以降

## インストール

Unity Package ManagerからGit URLで追加:
```
https://github.com/piti6/LightningProfiler.git?path=Packages/com.piti6.lightning-profiler
```

または、リポジトリをクローンして `Packages/com.piti6.lightning-profiler` フォルダをプロジェクトの `Packages/` ディレクトリにコピーしてください。

## 使い方

1. Profilerウィンドウを開く（**Window > Analysis > Profiler**）
2. モジュールのドロップダウンから **LightningProfiler CPU Usage** を選択
3. **Chart Filter** に閾値（ms）を設定してスパイク検出ボタンを有効化
4. **Pause on spike** / **Log on spike** を必要に応じて切り替え

## ライセンス

MIT
