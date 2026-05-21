---
name: x64-arm64
description: GitHub リリース時は win-x64 と win-arm64 の両アーキテクチャをビルドしてアセットに添付する
metadata: 
  node_type: memory
  type: feedback
  originSessionId: b84ba1c0-4a56-4a95-b443-283b8d13ae7d
---

リリースを作成するときは必ず x64 と arm64 の両方をビルドしてアップロードする。タグを打つ前に `XTimelineViewer.csproj` の `<Version>` と `Package.appxmanifest` の `Version=` を新しいバージョン番号に更新すること。

**Why:** ユーザーの明示的な指示。v1.3.1 でバージョン更新を忘れてタグを打ち直す羽目になった。

**How to apply:**
1. `XTimelineViewer.csproj` の `<Version>x.y.z</Version>` を更新
2. `Package.appxmanifest` の `Version="x.y.z.0"` を更新
3. コミット後にタグを打って push → CI が自動でビルド＆リリース作成

（`.github/workflows/release.yml` でタグ push をトリガーに x64/arm64 をビルドして GitHub Release を自動作成する）

```bash
# x64
dotnet publish XTimelineViewer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/

# arm64
dotnet publish XTimelineViewer.csproj -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true -p:Platform=arm64 -p:PlatformTarget=arm64 -o publish-arm64/
```
