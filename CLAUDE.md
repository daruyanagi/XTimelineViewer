# XTimelineViewer — 開発ガイド

複数の X（旧 Twitter）タイムラインを WebView2 ペインで横並び表示する Windows デスクトップアプリ。X の Web ページを細長い WebView2 に並べているだけで、公式 API は使わない。

## 技術スタック

- **WinUI 3 / Windows App SDK 1.6**（`WindowsAppSDKSelfContained` = 自己完結）、**.NET 8**（`net8.0-windows10.0.19041.0`、最小 OS 10.0.17763.0）
- ターゲット: **x64 / arm64**
- MVVM: `CommunityToolkit.Mvvm`、設定 UI に `CommunityToolkit.WinUI.Controls.SettingsControls`
- 描画コンテンツ: `Microsoft.Web.WebView2`（Edge Dev/WebView2 Runtime を利用）

## ビルド・実行

```powershell
dotnet build XTimelineViewer.csproj -c Debug -p:Platform=x64
```
出力: `bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/XTimelineViewer.exe`

- **デバッグ実行は `debug-run` スキルに従う**（ビルドした exe と起動する exe を必ず一致させる。古い exe 起動での時間浪費を防ぐ）。
- テスト: `dotnet test XTimelineViewer.Tests/XTimelineViewer.Tests.csproj`（xUnit）。CI では現状未実行（手動）。

## アーキテクチャ

- **`MainWindow` は機能ごとに分割した partial クラス**:
  - `MainWindow.xaml.cs` … 初期化・フィールド・共通処理（`ShowDialogAsync` など）
  - `MainWindow.Timeline.cs` … ペイン UI 構築、⚙ 設定ダイアログ、番号バッジ、フォーカス移動
  - `MainWindow.WebView2.cs` … WebView2 初期化、拡張機能読み込み、ホーム自動更新の JS 注入
  - `MainWindow.Post.cs` … 投稿ダイアログ（プリロード、アカウント切替、ESC/Ctrl+Enter 制御）
  - `MainWindow.HardReload.cs` … 定期ハードリロード（#49）と UI 更新タイマー
  - `MainWindow.Settings.cs` / `.Search.cs` / `.Profiles.cs` / `.Theme.cs` / `.Updates.cs` / `.Onboarding.cs`
- **WebView2 環境はプロファイル単位**: `GetOrCreateProfileEnvAsync(profileId)` が `CoreWebView2Environment` を生成・キャッシュ。データ保存先は `ProfileService`（#157）。
- **MVVM**: `SettingsViewModel` が `AppSettings` をラップ。設定ウィンドウの `SettingsChanged` で各 WebView / タイマーへ即時反映。
- **ホーム自動更新（#207）**: `x.com/home` に JS を注入し、先頭付近かつ非入力・非検索時のみ新着を取り込む。

## 規約（重要）

- **UI 文字列をコードに直接埋め込まない**。`Strings/ja-JP/Resources.resw` と `Strings/en-US/Resources.resw` に追加し、`R.Get("Key")` で参照する。**両言語のキーは常に一致**させる。UI 文字列の追加・変更は `ui-string` スキルに従う。
- **言語切り替え**: unpackaged では `PrimaryLanguageOverride`（MSIX パッケージ ID 必須）が使えないため、resw を直接パースする方式（#117）。
- **PUA グリフ**（Segoe Fluent Icons 等）は `\uXXXX` エスケープで書く。リンターが生グリフに変換すると Edit で扱いづらい。
- イシュー着手時は **ブランチを切る**。イシューは **`--assignee daruyanagi`**。実装前に **コメントまで読む**（`gh issue view <n> --comments`）。

## 配布・リリース

- **GitHub リリース（ZIP）と winget のみ**。**Microsoft Store 配布は廃止（#272）**。
- CI（`.github/workflows/release.yml`）が `v*` タグ push で ZIP ビルド → GitHub リリース → winget 公開を自動実行。手順は `release` スキル、バージョン更新は `Release.ps1`。
- **コマンドライン起動**: ZIP/winget には C++ の極小ランチャー **`xtv.exe`**（`tools/launcher/`、依存 DLL ゼロ）を同梱。`xtv` が主、`xtimelineviewer` は後方互換（#264）。
- **arm64 の落とし穴**: arm64 ビルド/publish には必ず **`-p:EffectivePlatform=arm64`** を渡す。付けないと WebView2 SDK がビルドホスト RID(win-x64) を見て x64 の `Microsoft.Web.WebView2.Core.dll` を arm64 パッケージに混入させ、arm64 で `BadImageFormatException` になる（#267）。
- `Package.appxmanifest` は自己完結ビルドに必要なため残す。`<Logo>StoreLogo.png</Logo>` は appx 必須要素かつ `AboutPage` で使用するので削除しない。
