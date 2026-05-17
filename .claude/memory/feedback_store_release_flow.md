---
name: feedback-store-release-flow
description: メジャー/マイナーリリース後に Microsoft Store 申請ページとパッケージフォルダーを開く
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 69aa5296-fff9-465c-a0e5-3ced266e0134
---

メジャー・マイナーリリース（バージョン x.y が繰り上がる場合）のあとは、以下を必ず実行する。

1. Microsoft Store パートナーセンターの申請ページをブラウザーで開く
2. x64・arm64 の .msix ファイルを `AppPackages\` 直下にコピーしてから、そのフォルダーをエクスプローラーで開く

ステップ 2 のコピー例（PowerShell）:
```powershell
Get-ChildItem ".\AppPackages" -Recurse -Filter "*.msix" |
    ForEach-Object { Copy-Item $_.FullName ".\AppPackages\" -Force }
Start-Process "explorer.exe" ".\AppPackages"
```

**Why:** 申請の手間を最小化するため。サブフォルダーを掘らずにすぐ .msix を選択してアップロードできるようにする。

**How to apply:** `x.y.0` 形式のタグを push してリリースを作成したあとに実施。ビルド番号のみ（x.y.z → x.y.z+1）の場合は不要。

Store アプリ ID: `9P308HB5BLJ1`
パートナーセンター URL: `https://partner.microsoft.com/dashboard/products/9P308HB5BLJ1/overview`
