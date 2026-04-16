# SpikeTrap

**[English](README.md)**

プラグイン可能なフレームフィルター、ハイライトストリップ、AI駆動プロファイリング自動化、マーク済みフレーム収集機能を備えた、Unity Editor向け高機能CPUプロファイラーモジュールです。

![SpikeTrap — フィルターストリップと階層ビューによるスパイク検出](Documentation~/spike-detection-overview.png)

## なぜ SpikeTrap？

Unityの標準CPUモジュールは**リングバッファ**方式 — 直近300〜2000フレームのみ保持し、古いフレームは自動的に破棄されます。60fpsで5〜33秒分の履歴しかありません。SpikeTrapはこれを**選択的キャプチャ**に置き換えます。フィルターにマッチしたフレームだけを保持するため、数分〜数時間のプロファイリングでもデータを失いません。

### SpikeTrap vs. 標準CPUモジュール

| | 標準CPUモジュール | SpikeTrap |
|---|---|---|
| **フレーム保持** | リングバッファ（300〜2000フレーム）— 古いフレームは上書き | 選択的: マッチしたフレームのみ保持、セッション長の制限なし |
| **10分 @ 60fps**（36,000フレーム） | 直近300〜2000のみ保持、95%以上を喪失 | 重要な〜20スパイクのみを保持 |
| **レアスパイク（1/10,000）** | 確認前にほぼ確実に上書きされる | フィルターが自動的にキャプチャ |
| **フィルター種別** | 組み込みなし | Spike（CPU時間）、GC（アロケーション量）、Search（マーカー名）、カスタム |
| **視覚インジケーター** | なし | フィルターごとの色分けハイライトストリップ + 前後ナビゲーション |
| **フィルター合成** | N/A | Match Any（OR）/ Match All（AND）+ 結果ストリップ |
| **自動化API** | 低レベルの `ProfilerDriver` + `HierarchyFrameDataView` — フレーム単位の走査、マーカーID手動解決 | `SpikeTrap.Editor.SpikeTrapApi` 静的クラス — 収集開始→待機→保存→分析、マーカー名解決済み |
| **AIコードファースト** | バッファ上書き前に常時ポーリングが必要 | `SpikeTrapApi.StartCollecting()` → 待機 → `SpikeTrapApi.StopCollectingAndSave()` → `SpikeTrapApi.GetSpikeFrames()` |
| **出力形式** | 標準 `.data`（全フレーム、ローリング） | 標準 `.data`（マッチフレームのみ、マージ可能） |
| **階層ビュー** | 組み込み | 継承 — 同じ階層ビュー、同じ詳細カラム |

### 選択的キャプチャの仕組み

標準プロファイラーは全フレームを固定サイズのリングバッファに記録します。バッファが一杯になると最も古いフレームは永久に失われます。

SpikeTrapは同じネイティブプロファイラーのデータストリームにフックし、各フレームをアクティブなフィルターでリアルタイム評価します。マッチしたフレーム（スパイク閾値超過、GCアロケーション過大、特定マーカー検出）のみが一時 `.data` ファイルに保存されます。セッション終了時にマッチフレームを1つのファイルにマージします。

つまり、360,000フレームを生成する100分間のプロファイリングセッションでも、保存されるのは50フレーム程度のスパイクのみ — 各フレームにフルコールスタック付きで、すぐに分析可能です。

## 機能

### フレームフィルター

3つの組み込みフィルター。それぞれハイライトストリップと前後ナビゲーションボタン付き:

| フィルター | 検出内容 | 単位切り替え | ストリップ色 |
|---|---|---|---|
| **Spike** | CPU時間が閾値を超えるフレーム | ms / s | 緑 |
| **GC** | GCアロケーションが閾値を超えるフレーム | KB / MB | 赤 |
| **Search** | 指定した名前のプロファイラーサンプルを含むフレーム（大文字小文字不問） | — | オレンジ |

![フィルターストリップによるスパイクとGCのライブ検出](Documentation~/filter-strips-live.png)

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

![Collect modeオーバーレイ](Documentation~/collect-mode.png)

### スクリーンショットプレビュー

[ScreenshotToUnityProfiler](https://github.com/wotakuro/ScreenshotToUnityProfiler) ランタイムパッケージで撮影されたフレーム単位のスクリーンショットを、プロファイラー詳細ビューにインライン表示します。スクリーンショットのないフレームをスクラブ中も直前の有効なスクリーンショットが表示され、新しい記録やファイル読み込み時にクリアされます。

ランタイムでスクリーンショットキャプチャを有効化:

```csharp
using SpikeTrap.Runtime;

// キャプチャ開始（デフォルト: 1/4解像度）
SpikeTrapApi.InitializeScreenshotCapture();

// スケール指定（0.5 = 半分の解像度）
SpikeTrapApi.InitializeScreenshotCapture(0.5f);

// カスタム圧縮
SpikeTrapApi.InitializeScreenshotCapture(0.25f, TextureCompress.JPG_BufferRGB565);

// キャプチャルーチンのオーバーライド（例: 特定カメラをキャプチャ）
SpikeTrapApi.InitializeScreenshotCapture(captureBehaviour: target =>
{
    Camera.main.targetTexture = target;
    Camera.main.Render();
    Camera.main.targetTexture = null;
});

// キャプチャ停止・リソース解放
SpikeTrapApi.DestroyScreenshotCapture();
```

### カスタムフィルター

`FrameFilterBase` を継承して独自のフィルターを作成し、`SpikeTrapApi` で登録:

```csharp
using SpikeTrap.Editor;
using UnityEngine;

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

// 登録（エディタスクリプトや [InitializeOnLoad] コンストラクタから呼び出し）
SpikeTrapApi.RegisterCustomFilterFactory(() => new MyFilter());
```

### スクリプティングAPI

`SpikeTrap.Editor.SpikeTrapApi` と `SpikeTrap.Runtime.SpikeTrapApi` 静的クラスがプロファイリング自動化、カスタムフィルター、スクリーンショットキャプチャのAPIを提供します:

```csharp
using SpikeTrap.Editor;

SpikeTrapApi.StartCollecting(spikeThresholdMs: 33f);
// ... ゲーム実行中、スパイクが自動キャプチャ ...
SpikeTrapApi.StopCollectingAndSave("/path/to/spikes.data");

FrameSummary[] spikes = SpikeTrapApi.GetSpikeFrames(33f);
foreach (var s in spikes)
    Debug.Log(s); // "Frame 296: 2038.40ms, GC 5.7KB | NavMeshManager=1953.89ms, ..."
```

## アーキテクチャ

### プラグイン可能なフィルターシステム

フィルターは `IFrameFilter` を実装（または `FrameFilterBase` を継承）します。コントローラがネイティブAPIアクセス、キャッシング、マッチフレーム追跡をすべて管理します。

```
ネイティブAPI（メインスレッド）      マネージドキャッシュ          フィルター（スレッドセーフ）
ProfilerDriver.GetRawFrameDataView  -->  CachedFrameData  -->  filter.Matches()
  セッションごとに1フレーム1回         { EffectiveTimeMs,     純マネージド,
  1パスで全データを抽出                  GcAllocBytes,        大きなフレーム範囲で
                                         UniqueMarkerIds }    並列化
```

**`CachedFrameData`** は1フレームから抽出されたフィルター関連データを含むreadonly構造体です。フィルターはこの事前抽出データを受け取り、ネイティブAPIには一切触れません。

**`IFrameFilter`** インターフェース:

```csharp
public interface IFrameFilter : IDisposable
{
    Color HighlightColor { get; }
    bool IsActive { get; }
    bool DrawToolbarControls();
    bool Matches(in CachedFrameData frameData);
    void InvalidateCache();
}
```

### パフォーマンス

- **セッションごとに1フレーム1回のネイティブ呼び出し**: `GetRawFrameDataView` はフレームごとに1回のみ呼ばれます。フィルターデータ（CPU時間、GCバイト、マーカーID）は単一のサンプルイテレーションループで抽出されます。
- **キャッシュされたフレームデータ**: 抽出データは `ConcurrentDictionary` に格納。閾値/検索の変更はキャッシュ済みマネージドデータを再評価し、ネイティブAPI呼び出しは不要。
- **並列マッチング**: 500フレーム以上のフルリスキャンは `Parallel.For` を使用。`OnMarkerDiscovered` と `Matches` はスレッドセーフ。
- **マーカーIDキャッシング**: 検索フィルターはユニークIDごとに1回だけマーカー名を解決。以降のチェックは `ConcurrentDictionary` 経由の整数比較。
- **分割抽出**: 大きな `.data` ファイルの読み込みはエディタフレームごとに50フレームずつ抽出し、ブロッキングを回避。
- **セッション認識キャッシング**: キャッシュは `frameStartTimeNs` をセッションフィンガープリントとして使用。同じ `.data` ファイルを複数回読み込んでも同じキャッシュを共有（A→B→A でAのキャッシュを再利用）。

### スレッドセーフティ

| コンポーネント | スレッドセーフ | メカニズム |
|---|---|---|
| `SearchFrameFilter.Matches` | Yes | 単一volatile `SearchState` 参照、ローカルキャプチャ |
| `SearchFrameFilter.OnMarkerDiscovered` | Yes | `ConcurrentDictionary.TryAdd`、ローカルステートキャプチャ（検索フィルター内部） |
| `SpikeFrameFilter.Matches` | Yes | 不変の `CachedFrameData` フィールドのみ読み取り |
| `GcFrameFilter.Matches` | Yes | 不変の `CachedFrameData` フィールドのみ読み取り |
| `s_FrameDataCache` | Yes | `ConcurrentDictionary` |
| `s_MarkerNames` | Yes | `ConcurrentDictionary` |
| `CollectMatchingFrames` | Yes | `Parallel.For` + `ConcurrentBag` による結果収集 |
| ネイティブAPI抽出 | メインスレッドのみ | `GetRawFrameDataView`, `ProfilerFrameDataIterator` |

## 要件

- Unity 2022.3+

## インストール

Unity Package ManagerからGit URLで追加:

```
https://github.com/piti6/SpikeTrap.git?path=Packages/com.piti6.spike-trap
```

または、リポジトリをクローンして `Packages/com.piti6.spike-trap` をプロジェクトの `Packages/` ディレクトリにコピーしてください。

## クイックスタート

1. Profilerウィンドウを開く（**Window > Analysis > Profiler**）
2. モジュールのドロップダウンから **SpikeTrap CPU Usage** を選択
3. 各ストリップ行でフィルター閾値を設定（Spike ms、GC KB、検索語）
4. **Match any** または **Match all** をドロップダウンから選択
5. **Pause on match** / **Log on match** を必要に応じて切り替え
6. **Collect** ボタンでマッチしたフレームのみを記録

## 開発

このリポジトリはUnityプロジェクトです。`Assets/ProfilerStressTest.cs` がランダムなCPUスパイク、GCプレッシャー、名前付きプロファイラーサンプル（`HeavyComputation`、`GarbageBlast`、`NetworkSync`、`SaveCheckpoint`）を生成し、フィルターのテストに使用できます。

テストはUnity Test Runner（EditMode、アセンブリ `SpikeTrap.Tests`）で実行してください。

## ライセンス

MIT
