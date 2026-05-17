---
name: リリースビルドは x64 と arm64 の両方を含める
description: GitHub リリース時は win-x64 と win-arm64 の両アーキテクチャをビルドしてアセットに添付する
type: feedback
---

リリースを作成するときは必ず x64 と arm64 の両方をビルドしてアップロードする。

**Why:** ユーザーの明示的な指示。

**How to apply:** `dotnet publish` を2回実行する（`-r win-x64` と `-r win-arm64`）。arm64 は `-p:PlatformTarget=arm64` も必要。それぞれ extensions をコピーして zip 化し、GitHub Release にアセットとして添付する。

```bash
# x64
dotnet publish XTimelineViewer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/

# arm64
dotnet publish XTimelineViewer.csproj -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true -p:Platform=arm64 -p:PlatformTarget=arm64 -o publish-arm64/
```
