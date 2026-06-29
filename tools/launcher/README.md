# xtv.exe — コマンドライン起動用ランチャー（#264）

`XTimelineViewer.exe`（.NET self-contained）を起動するための、**依存 DLL を持たない極小ネイティブ exe** です。

## なぜ必要か

winget の portable (ZIP) インストールは `PortableCommandAlias` を **symlink** で作成します。
しかし .NET self-contained の apphost は **symlink の場所を基準に DLL を探す**ため、本体
`XTimelineViewer.exe` を symlink 経由で直接実行すると DLL 解決に失敗します（ターミナルから
`xtimelineviewer` が起動できない）。

`xtv.exe` は依存 DLL を持たないため symlink 経由でも問題なく起動し、自分の**実体パスを
symlink 越しに解決**して、隣にある `XTimelineViewer.exe` を**正しい作業ディレクトリ**で
起動します。コマンドライン引数はそのまま本体へ転送します。

## 配布

- ZIP（GitHub リリース）に `XTimelineViewer.exe` と並べて同梱（CI: `.github/workflows/release.yml`）。
- winget マニフェスト（microsoft/winget-pkgs）の `NestedInstallerFiles` は、エイリアス
  `xtv` / `xtimelineviewer` の両方を **`RelativeFilePath: xtv.exe`** に向ける。
- Store(MSIX) 版は `Package.appxmanifest` の `appExecutionAlias`（#262）で対応済みのため、
  このランチャーは不要。

## ビルド（一度だけ。成果物 `xtv.exe` はリポジトリにコミットし、CI では再ビルドしない）

VS の x64 ネイティブツール環境で:

```bat
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
cl /nologo /utf-8 /O1 /MT /EHsc /DUNICODE /D_UNICODE xtv.cpp /Fe:xtv.exe /link /SUBSYSTEM:WINDOWS Shell32.lib
```

- `/MT`：CRT を静的リンク＝VC ランタイム DLL に非依存。
- `/SUBSYSTEM:WINDOWS`：コンソール窓を出さない。
- `/utf-8`：日本語コメントを含むソースを正しく読ませる。
- 依存は `SHELL32.dll` / `KERNEL32.dll`（いずれも OS 標準）のみ。

## アーキテクチャ

現状コミットしている `xtv.exe` は **x64** です。arm64 Windows では x64 エミュレーションで
動作します（ランチャーは極小のため実害なし。本体アプリは arm64 ネイティブ ZIP のまま）。
arm64 ネイティブ版が必要になったら、VS の「arm64 ビルドツール」を入れて
`vcvarsamd64_arm64.bat` でクロスビルドする。
