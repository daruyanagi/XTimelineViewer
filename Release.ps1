<#
.SYNOPSIS
    XTimelineViewer の Microsoft Store 用リリース成果物を生成する（#216）。

.DESCRIPTION
    役割分担:
      - GitHub 配布（ZIP）と winget 公開は CI（.github/workflows/release.yml）が
        v* タグの push をトリガーに自動で行う。本スクリプトでは扱わない。
      - 本スクリプトは CI が扱わない Microsoft Store 用の成果物を担当する。

    手作業フローで発生したミス（arm64 の RuntimeIdentifier 明示漏れ、
    x64/arm64 の並行ビルドによる obj 衝突）を防ぐため、逐次・自動で行う:

      1. （任意）csproj と Package.appxmanifest のバージョンを一括更新
      2. Store 用 MSIX を x64 / arm64 で生成（GenerateAppxPackageOnBuild）し、
         winapp（makeappx）で 1 つの .msixbundle に束ねる（#216 経路B：小サイズ）

    成果物は publish\release\ に集約する。

.PARAMETER Version
    新しいバージョン（例 1.6.1）。指定すると csproj と appxmanifest を更新する。
    省略時は csproj の現在値を使う（ファイルは変更しない）。

.PARAMETER WithZip
    ローカル検証用に GitHub 配布相当の ZIP（自己完結・アンパッケージド）も生成する。
    本番の GitHub リリース ZIP は CI が作るため通常は不要。

.PARAMETER SkipBundle
    Store 用 MSIX / バンドルの生成をスキップする（バージョン更新や ZIP だけ行いたい場合）。

.EXAMPLE
    .\Release.ps1 -Version 1.6.1   # バージョン更新 + Store 用 .msixbundle を生成
    .\Release.ps1                  # 現行バージョンのまま .msixbundle を生成
    .\Release.ps1 -WithZip         # ローカル検証用に ZIP も生成
#>
[CmdletBinding()]
param(
    [string] $Version,
    [switch] $WithZip,
    [switch] $SkipBundle
)

$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
$proj    = Join-Path $root 'XTimelineViewer.csproj'
$manifest= Join-Path $root 'Package.appxmanifest'
$tfm     = 'net8.0-windows10.0.19041.0'
$outDir  = Join-Path $root 'publish\release'

function Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

# ── バージョン解決（必要なら更新） ──────────────────────────────────────────────
Step 'バージョン'
if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version は x.y.z 形式で指定してください: $Version" }
    (Get-Content $proj -Raw) -replace '<Version>[\d.]+</Version>', "<Version>$Version</Version>" |
        Set-Content $proj -Encoding utf8 -NoNewline
    # Identity の Version のみを置換する。-creplace（大文字小文字を区別）で XML 宣言の
    # 小文字 version="1.0" を除外し、ProcessorArchitecture への先読みで MinVersion 等を除外する。
    (Get-Content $manifest -Raw) -creplace 'Version="[\d.]+"(?=\s+ProcessorArchitecture)', "Version=`"$Version.0`"" |
        Set-Content $manifest -Encoding utf8 -NoNewline
    Write-Host "csproj / appxmanifest を $Version に更新しました"
} else {
    if ((Get-Content $proj -Raw) -match '<Version>([\d.]+)</Version>') { $Version = $Matches[1] }
    else { throw 'csproj から Version を読み取れませんでした' }
}
$msixVer = "$Version.0"
Write-Host "対象バージョン: $Version (MSIX: $msixVer)"

# 出力先を初期化
Remove-Item $outDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $outDir | Out-Null

# 実行中インスタンスがあるとビルドがファイルロックで失敗するため止める
try { Stop-Process -Name XTimelineViewer -Force -ErrorAction Stop } catch {}

# ── ローカル検証用 ZIP（自己完結・アンパッケージド。本番は CI が生成） ─────────────
if ($WithZip) {
    foreach ($rid in 'win-x64', 'win-arm64') {
        $plat = if ($rid -eq 'win-x64') { 'x64' } else { 'arm64' }
        Step "ZIP 発行: $rid"
        $pubDir = Join-Path $root "publish\$rid"
        # EffectivePlatform を明示しないと、WebView2 SDK がビルドホストの RID(win-x64) を見て
        # x64 の Microsoft.Web.WebView2.Core.dll を arm64 出力に混入させ、arm64 で BadImageFormat になる。
        dotnet publish $proj -c Release -r $rid -p:PlatformTarget=$plat -p:EffectivePlatform=$plat -p:WindowsPackageType=None -o $pubDir
        if ($LASTEXITCODE -ne 0) { throw "publish 失敗: $rid" }
        # コマンドライン起動用ランチャーを同梱（#264。CI の release.yml と揃える）
        Copy-Item (Join-Path $root 'tools\launcher\xtv.exe') (Join-Path $pubDir 'xtv.exe') -Force
        $zip = Join-Path $outDir "XTimelineViewer-$Version-$rid.zip"
        Compress-Archive -Path "$pubDir\*" -DestinationPath $zip -Force
        Write-Host "→ $zip"
    }
}

# ── Store 用 MSIX → .msixbundle（#216 経路B） ──────────────────────────────────
if (-not $SkipBundle) {
    $msixPaths = @()
    foreach ($plat in 'x64', 'arm64') {
        $rid = "win-$plat"
        Step "MSIX ビルド: $plat"
        # arm64 は RuntimeIdentifier を明示しないと NETSDK1032（RID win-x64 と PlatformTarget arm64 の不一致）になる。
        # EffectivePlatform を明示しないと、WebView2 SDK がビルドホストの RID(win-x64) を見て x64 の
        # Microsoft.Web.WebView2.Core.dll を arm64 パッケージに混入させ、arm64 で BadImageFormat になる。
        dotnet build $proj -c Release -p:Platform=$plat -p:PlatformTarget=$plat `
            -p:RuntimeIdentifier=$rid -p:EffectivePlatform=$plat -p:GenerateAppxPackageOnBuild=true
        if ($LASTEXITCODE -ne 0) { throw "MSIX ビルド失敗: $plat" }

        $msix = Get-ChildItem (Join-Path $root "bin\$plat\Release\$tfm\$rid\AppPackages") `
            -Recurse -Filter "XTimelineViewer_${msixVer}_$plat.msix" -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if (-not $msix) { throw "生成された MSIX が見つかりません: $plat ($msixVer)" }
        Copy-Item $msix.FullName $outDir
        $msixPaths += (Join-Path $outDir $msix.Name)
        Write-Host "→ $($msix.Name)"
    }

    Step 'MSIX バンドル生成'
    # 既存のキュレート済み .msix を束ねるだけ（再パッケージしないので小サイズ）
    $stage = Join-Path $root 'publish\_bundle'
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $stage | Out-Null
    $msixPaths | ForEach-Object { Copy-Item $_ $stage }

    $bundle = Join-Path $outDir "XTimelineViewer-$Version.msixbundle"
    winapp tool makeappx bundle /d $stage /p $bundle /o
    if ($LASTEXITCODE -ne 0) { throw 'makeappx bundle 失敗' }
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "→ $bundle（未署名。Store はこのままアップロード可）"
}

# ── まとめ ──────────────────────────────────────────────────────────────────────
Step '完了：成果物'
Get-ChildItem $outDir | Select-Object Name, @{N='MB';E={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize

Write-Host @"
次の手順（手動）:
  1. バージョン更新分をコミット & PR → main にマージ
  2. v$Version タグを push（または gh release create v$Version）
     → CI が GitHub リリースの ZIP と winget 公開を自動で行う
  3. （マイナー以上）Microsoft Store パートナーセンターに
     publish\release\XTimelineViewer-$Version.msixbundle をアップロード
"@ -ForegroundColor Yellow
