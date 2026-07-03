---
name: release
description: XTimelineViewer のリリース手順。バージョンを上げて GitHub リリース（ZIP）と winget を CI 経由で公開する。ユーザーが「リリースしよう」「パッチ/マイナーリリース」「バージョンを上げて公開」等と言ったときに使う。
---

# XTimelineViewer リリース手順

## 役割分担（重要）
- **GitHub リリース（ZIP）と winget 公開は CI が自動**で行う（`.github/workflows/release.yml`、`v*` タグ push がトリガー）。手動で ZIP をビルド・添付しない。
- **`Release.ps1`（リポジトリ直下）はバージョン更新**（`XTimelineViewer.csproj` / `Package.appxmanifest`）を担当。任意で `-WithZip` によりローカル検証用 ZIP も生成できる。
- **PR のマージはユーザーが行う**。エージェントは self-merge しない。
- **Microsoft Store 配布は廃止した（#272）**。Store 申請ステップは行わない。

## バージョン種別
- **パッチ (x.y.Z+1)**: バグ修正のみ。
- **マイナー (x.Y+1.0) 以上**: 新機能あり。

いずれも配布経路は同じ（GitHub ZIP + winget）。種別はリリースノートの粒度の目安として使う。

## 手順

### 1. ブランチを切る
```
git checkout main && git pull
git checkout -b chore/release-vX.Y.Z
```

### 2. バージョン更新
```powershell
.\Release.ps1 -Version X.Y.Z
```
`Release.ps1` が `XTimelineViewer.csproj` の `<Version>` と `Package.appxmanifest` の Identity `Version="X.Y.Z.0"` を更新する。更新後、両ファイルが正しいか確認する（XML 宣言や MinVersion を壊していないこと）。

### 3. コミット → PR → マージ
バージョン更新分をコミットし PR を作成。前回リリース以降のコミットを拾い、リリースノートの材料にする。**ユーザーがマージするのを待つ**。

### 4. タグ push（マージ後）
```
git checkout main && git pull
git tag vX.Y.Z
git push origin vX.Y.Z
```
これで CI が ZIP ビルド → GitHub リリース作成（generate-notes）→ winget 公開を自動実行する。`gh run watch <id>` で完了を確認し、`gh release view vX.Y.Z` で公開を確認する。

## 注意
- バージョンは csproj（3桁）と appxmanifest（4桁 `X.Y.Z.0`）で食い違わせない。過去 v1.3.1 で更新漏れによりタグを打ち直した。
- arm64 の ZIP に x64 の WebView2 が混入すると arm64 端末で `BadImageFormatException` になる。`Release.ps1` / CI は `-p:EffectivePlatform=<arch>` を指定済み（#267）。手を入れる場合は崩さないこと。
