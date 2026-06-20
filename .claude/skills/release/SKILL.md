---
name: release
description: XTimelineViewer のリリース手順。バージョンを上げて GitHub リリース（CI）と、マイナー以上は Microsoft Store 申請まで行う。ユーザーが「リリースしよう」「パッチ/マイナーリリース」「バージョンを上げて公開」等と言ったときに使う。
---

# XTimelineViewer リリース手順

## 役割分担（重要）
- **GitHub リリース（ZIP）と winget 公開は CI が自動**で行う（`.github/workflows/release.yml`、`v*` タグ push がトリガー）。手動で ZIP をビルド・添付しない。
- **`Release.ps1`（リポジトリ直下）は Store 用 `.msixbundle` とバージョン更新を担当**。
- **PR のマージはユーザーが行う**。エージェントは self-merge しない。

## バージョン種別の判断
- **パッチ (x.y.Z+1)**: バグ修正のみ。Store 申請は**不要**。
- **マイナー (x.Y+1.0) 以上**: 新機能あり。Store 申請が**必須**。

## 手順

### 1. ブランチを切る
```
git checkout main && git pull
git checkout -b chore/release-vX.Y.Z
```

### 2. バージョン更新（+ パッチは Store ビルドを省略）
```powershell
# パッチ（Store 不要）: バージョンだけ更新
.\Release.ps1 -Version X.Y.Z -SkipBundle

# マイナー以上（Store 用 .msixbundle も生成）
.\Release.ps1 -Version X.Y.Z
```
`Release.ps1` が `XTimelineViewer.csproj` の `<Version>` と `Package.appxmanifest` の Identity `Version="X.Y.Z.0"` を更新する。更新後、両ファイルが正しいか確認する（XML 宣言や MinVersion を壊していないこと）。

### 3. コミット → PR → マージ
バージョン更新分をコミットし PR を作成。`gh pr view <n>..HEAD` 等で v1.x.0 以降のコミットを拾い、リリースノートの材料にする。**ユーザーがマージするのを待つ**。

### 4. タグ push（マージ後）
```
git checkout main && git pull
git tag vX.Y.Z
git push origin vX.Y.Z
```
これで CI が ZIP ビルド → GitHub リリース作成（generate-notes）→ winget 公開を自動実行する。`gh run watch <id>` で完了を確認し、`gh release view vX.Y.Z` で公開を確認する。

### 5. （マイナー以上のみ）Microsoft Store 申請
- `Release.ps1`（手順2でフル実行）が `publish\release\XTimelineViewer-X.Y.Z.msixbundle` を生成済み（未署名・そのままアップロード可、x64+arm64 内包）。
- パートナーセンターにアップロード:
  - アプリ ID `9P308HB5BLJ1`
  - URL `https://partner.microsoft.com/dashboard/products/9P308HB5BLJ1/overview`
- Store のリリースノートは、前回 Store 申請バージョン以降の**ユーザー向け**変更を ja / en で用意する（内部リファクタリングは 1 行に集約してよい）。

## 注意
- バージョンは csproj（3桁）と appxmanifest（4桁 `X.Y.Z.0`）で食い違わせない。過去 v1.3.1 で更新漏れによりタグを打ち直した。
- パッチで誤って Store にフルビルドする必要はない（`-SkipBundle`）。
