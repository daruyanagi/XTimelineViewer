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

**既定はパッチ (x.y.Z+1)。新機能が入っていてもパッチで進める。**

マイナー (x.Y+1.0) は「十分に安定してから」上げる方針。パッチを刻んで配りながら、区切りがついた時点でマイナーに上げる。**種別をその都度確認しない**（変更規模が大きいときに一言添える程度はよい）。

いずれも配布経路は同じ（GitHub ZIP + winget）。

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

### 5. winget-pkgs の PR を確認する（**忘れやすい**）

**CI の「Publish to winget」ステップが success でも、winget に載ったとは限らない。**
このステップは microsoft/winget-pkgs へ PR を作るところまでしかやらない。その後
Microsoft 側の検証を通り、モデレーターにマージされて初めて公開される。

```powershell
gh pr list --repo microsoft/winget-pkgs --search "daruyanagi.XTimelineViewer" --state all --limit 5 `
  --json number,title,state,labels
```

`Validation-Defender-Error` などが付いていたら止まっている。過去に v2.0.1 / v2.0.3 / v2.0.4 が
これで止まり、**v2.0.3 は気づかないまま次のリリースへ進んでしまった**（winget が 2.0.2 のまま
取り残された）。

判定は数時間〜1 日ほどかかるので、タグ push 直後に見て `OPEN` なら後で見直す。

## 注意
- バージョンは csproj（3桁）と appxmanifest（4桁 `X.Y.Z.0`）で食い違わせない。過去 v1.3.1 で更新漏れによりタグを打ち直した。
- arm64 の ZIP に x64 の WebView2 が混入すると arm64 端末で `BadImageFormatException` になる。`Release.ps1` / CI は `-p:EffectivePlatform=<arch>` を指定済み（#267）。手を入れる場合は崩さないこと。
- **`xtv.exe`（`tools/launcher/`）は Defender の ML ヒューリスティックに `Trojan:Win32/Wacatac.B!ml` として拾われやすい**（無署名・発行元不明・CRT 静的リンクの小さなネイティブ exe が別プロセスを起こすため。#383）。
  - 誤検知報告は**ハッシュ単位**で効く。`xtv.exe` を作り直すと前の報告は無効になるので、**バイナリを差し替えてから報告する**こと。順序を逆にすると報告が無駄になる（実際にやってしまった）。
  - 検出は決定的ではない。**同一バイナリでも winget の検証を通るときと落ちるときがある**（v2.0.1 は落ちて v2.0.2 は通った）。落ちたら誤検知報告を出し、PR にコメントして再検証を依頼する。
  - 本命の対策は署名（#336）。
