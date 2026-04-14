# Lightning Profiler

**[English](README.md)**

プラグイン可能なフレームフィルター、ハイライトストリップ、マーク済みフレーム収集機能を備えた、Unity Editor向け高機能CPUプロファイラーモジュールです。

## 機能

### フレームフィルター

3つの組み込みフィルター。それぞれハイライトストリップと前後ナビゲーションボタン付き:

| フィルター | 検出内容 | 単位切り替え | ストリップ色 |
|---|---|---|---|
| **Spike** | CPU時間が閾値を超えるフレーム | ms / s | 緑 |
| **GC** | GCアロケーションが閾値を超えるフレーム | KB / MB | 赤 |
| **Search** | 指定した名前のプロファイラーサンプルを含むフレーム（大文字小文字不問） | — | オレンジ |

### Match Any / Match All

ツールバーのドロップダウンでフィルターの組み合わせ方を制御:

- **Match any**（OR）— いずれかのアクティブなフィルターがマッチすれば対象。デフォルト動作。
- **Match all**（AND）— すべてのアクティブなフィルターがマッチした場合のみ対象。交差結果を示す **Result** ストリップが表示され、独自の前後ナビゲーション付き。

非アクティブなフィルター（閾値=0 または検索語が空）はスキップされ、結果に影響しません。

### Pause & Log on Match

- **Pause on match** — フィルターがマッチした際にPlay Modeを自動停止
- **Log on match** — マッチしたフレームのコールスタック階層をコンソールに出力

### マーク済みフレーム収集

アクティブなフィルターにマッチしたフレームのみを `.data` ファイルに記録:

1. フィルターを設定（スパイク閾値、GC閾値、検索語）
2. **Collect** ボタンを押す — チャートと詳細エリアに「Collecting...」オーバーレイを表示
3. マッチしたフレームが順次テンプファイルに保存
4. **Save (N)** で収集フレームを1つの `.data` ファイルに統合保存、または **Stop** で保存せず終了

### スクリーンショットプレビュー

`screenshot2profiler` ランタイムパッケージで撮影されたフレーム単位のスクリーンショットを、プロファイラー詳細ビューにインライン表示します。スクリーンショットのないフレームをスクラブ中も直前の有効なスクリーンショットが表示され、新しい記録やファイル読み込み時にクリアされます。

### カスタムフィルター

`FrameFilterBase` を継承して独自のフィルターを作成:

```csharp
public class MyFilter : FrameFilterBase
{
    public override Color HighlightColor => Color.cyan;
    public override bool IsActive => true;

    public override bool Matches(in CachedFrameData frameData)
    {
        // スレッドセーフ必須 — Parallel.Forから呼ばれます
        return frameData.EffectiveTimeMs > 16f && frameData.GcAllocBytes > 1024;
    }
}
```

`[InitializeOnLoad]` で登録:

```csharp
[InitializeOnLoad]
static class MyFilterRegistration
{
    static MyFilterRegistration()
    {
        CpuUsageBridgeDetailsViewController.RegisterCustomFilterFactory(() => new MyFilter());
    }
}
```

## 要件

- Unity 2022.3+

## インストール

Unity Package ManagerからGit URLで追加:

```
https://github.com/piti6/LightningProfiler.git?path=Packages/com.piti6.lightning-profiler
```

または、リポジトリをクローンして `Packages/com.piti6.lightning-profiler` をプロジェクトの `Packages/` ディレクトリにコピーしてください。

## クイックスタート

1. Profilerウィンドウを開く（**Window > Analysis > Profiler**）
2. モジュールのドロップダウンから **LightningProfiler CPU Usage** を選択
3. 各ストリップ行でフィルター閾値を設定（Spike ms、GC KB、検索語）
4. **Match any** または **Match all** をドロップダウンから選択
5. **Pause on match** / **Log on match** を必要に応じて切り替え
6. **Collect** ボタンでマッチしたフレームのみを記録

## ドキュメント

アーキテクチャ、パフォーマンス特性、スレッドセーフティモデル、テストカバレッジの詳細は[パッケージREADME](Packages/com.piti6.lightning-profiler/README.md)を参照してください。

## 開発

このリポジトリはUnityプロジェクトです。`Assets/NewBehaviourScript.cs` がランダムなCPUスパイク、GCプレッシャー、名前付きプロファイラーサンプル（`HeavyComputation`、`GarbageBlast`、`NetworkSync`、`SaveCheckpoint`）を生成し、フィルターのテストに使用できます。

テストはUnity Test Runner（EditMode、アセンブリ `LightningProfiler.Tests`）で実行してください。

## ライセンス

MIT
