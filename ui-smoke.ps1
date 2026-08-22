# ui-smoke.ps1 — XTimelineViewer 起動スモークテスト (#346)
#
# 目的: 「壊滅的に壊れていないこと」だけを短時間で確認する。
#   - アプリが起動してメインウィンドウが出る
#   - ツールバーの主要ボタンが存在する
#   - メニューが開き、設定ウィンドウを開ける
#   - 設定のナビゲーションと主要コントロールが存在する
#   - タイムラインペインの番号バッジが 1..N の連番になっている
#
# 注意: ペインのテストは timelines.json があるときだけ実行される。
# CI ランナーには X ログインもタイムライン設定も無いのでペインは 0 件になり、
# このセクションは実質ローカル実行専用になる。CI で効く構造の検査は
# XTimelineViewer.Tests/TimelinePaneStructureTests.cs 側で行う。
# 機能の詳細な検証（テーマ切り替えの結果など）は意図的に含めない。UI の些細な
# 変更で落ちると保守されなくなり、旧 ui-tests.ps1 と同じ末路をたどるため。
#
# 使い方:
#   .\ui-smoke.ps1                     # 自分でビルド済み exe を探して起動〜終了まで
#   .\ui-smoke.ps1 -AppPid <PID>       # 既に起動しているプロセスを対象にする
#   .\ui-smoke.ps1 -ExePath <path>     # exe を明示
#
# 必要: winapp CLI v0.6+ (winget install Microsoft.WinAppCli)

[CmdletBinding()]
param(
    [int]$AppPid,
    [string]$ExePath,
    [string]$ScreenshotDir = "test-screenshots",
    [switch]$KeepRunning
)

$ErrorActionPreference = 'Continue'
$script:pass = 0
$script:fail = 0
$script:failures = @()
$launched = $null


# winapp は AutomationId が見つからないとツリー全体（WebView2 の中身を含む）を
# 走査し、-t を超えて数分戻らないことがある。プロセスごと打ち切って FAIL にする。
function Test-PaneId {
    param([string]$Id, [int]$TimeoutSec = 20)
    $proc = Start-Process -FilePath 'winapp' -PassThru -WindowStyle Hidden `
        -ArgumentList @('ui', 'wait-for', $Id, '-a', $AppPid, '-t', '8000')
    if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
        try { $proc.Kill() } catch { }
        Write-Output "'$Id' が $TimeoutSec 秒以内に見つかりません（打ち切り）"
        $global:LASTEXITCODE = 1
        return
    }
    $global:LASTEXITCODE = $proc.ExitCode
}

function Write-Section($name) { Write-Host "`n[$name]" -ForegroundColor Cyan }

function Test-Smoke {
    param([string]$Name, [scriptblock]$Script)
    try {
        $output = & $Script 2>&1
        $ec = $LASTEXITCODE
        if ($ec -eq 0) {
            $script:pass++
            Write-Host "  PASS: $Name" -ForegroundColor Green
        } else {
            $script:fail++
            $detail = ($output | Out-String).Trim()
            $script:failures += "$Name`n    $detail"
            Write-Host "  FAIL: $Name" -ForegroundColor Red
            if ($detail) { Write-Host "    $detail" -ForegroundColor DarkGray }
        }
    } catch {
        $script:fail++
        $script:failures += "$Name`n    $_"
        Write-Host "  FAIL: $Name — $_" -ForegroundColor Red
    }
}

# ── 前提チェック ──────────────────────────────────────────────────────────────
if (-not (Get-Command winapp -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: winapp CLI が見つかりません。'winget install Microsoft.WinAppCli' を実行してください。" -ForegroundColor Red
    exit 1
}

# ── 対象プロセスの用意 ────────────────────────────────────────────────────────
if (-not $AppPid) {
    if (-not $ExePath) {
        $ExePath = Get-ChildItem (Join-Path $PSScriptRoot "bin") -Recurse -Filter XTimelineViewer.exe -ErrorAction SilentlyContinue |
                   Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $ExePath -or -not (Test-Path $ExePath)) {
        Write-Host "ERROR: XTimelineViewer.exe が見つかりません。先にビルドするか -ExePath を指定してください。" -ForegroundColor Red
        exit 1
    }
    Write-Host "起動: $ExePath"
    $launched = Start-Process $ExePath -PassThru
    $AppPid = $launched.Id
    Start-Sleep -Seconds 10   # WebView2 の初期化を待つ
}

Write-Host "テスト対象 PID: $AppPid"
New-Item -ItemType Directory -Force -Path $ScreenshotDir | Out-Null

function Get-AppWindows {
    winapp ui list-windows --json 2>$null | ConvertFrom-Json |
        Where-Object { $_.processId -eq $AppPid }
}

# メインウィンドウ（タイトルは製品名で固定なのでロケール非依存）
function Get-MainWindow {
    Get-AppWindows | Where-Object { $_.className -eq 'WinUIDesktopWin32WindowClass' -and $_.title -like 'XTimelineViewer*' } |
        Select-Object -First 1
}

# 設定ウィンドウ。タイトルはロケールで変わる（ja: アプリ設定 / en: App Settings）ため、
# 「メインウィンドウ以外の WinUI トップレベルウィンドウ」として探す。
# PopupHost（メニュー等）は className が異なるので除外される。
function Get-SettingsWindow {
    $main = Get-MainWindow
    Get-AppWindows | Where-Object {
        $_.className -eq 'WinUIDesktopWin32WindowClass' -and $_.hwnd -ne $main.hwnd
    } | Select-Object -First 1
}

# ── 1. 起動とメインウィンドウ ─────────────────────────────────────────────────
Write-Section "起動"

Test-Smoke "プロセスが生存している" {
    $p = Get-Process -Id $AppPid -ErrorAction SilentlyContinue
    if ($p -and -not $p.HasExited) { $global:LASTEXITCODE = 0 } else { Write-Output "プロセスが終了している"; $global:LASTEXITCODE = 1 }
}

Test-Smoke "メインウィンドウが存在する" {
    if (Get-MainWindow) { $global:LASTEXITCODE = 0 }
    else { Write-Output "メインウィンドウが見つからない"; $global:LASTEXITCODE = 1 }
}

$main = Get-MainWindow
if ($main) { winapp ui screenshot window -a $AppPid -w $main.hwnd -o (Join-Path $ScreenshotDir "01-main.png") 2>$null | Out-Null }

# ── 2. ツールバー ─────────────────────────────────────────────────────────────
Write-Section "ツールバー"
Test-Smoke "投稿ボタン (PostBtn)"     { winapp ui wait-for "PostBtn"    -a $AppPid -t 5000 }
Test-Smoke "メニューボタン (AppMenuBtn)" { winapp ui wait-for "AppMenuBtn" -a $AppPid -t 5000 }

# ── 3. タイムラインペイン ──────────────────────────────────────
# 番号バッジは「左から N 番目」を表す約束（#225）で、Ctrl+数字 の飛び先と対応する。
# 削除や並べ替えのあとに振り直しが漏れるとここが飛ぶ（#359）。
Write-Section "タイムラインペイン"

$timelinesJson = Join-Path (Join-Path $env:LOCALAPPDATA 'XTimelineViewer') 'timelines.json'
$paneCount = 0
if (Test-Path $timelinesJson) {
    try { $paneCount = @(Get-Content $timelinesJson -Raw | ConvertFrom-Json).Count } catch { $paneCount = 0 }
}

if ($paneCount -eq 0) {
    Write-Host "  SKIP: タイムラインが 0 件（timelines.json なし）。ペインの検査を飛ばします" -ForegroundColor DarkGray
} else {
    # 番号バッジは 9 番まで。それ以降は非表示なので検査対象外。
    $checkCount = [Math]::Min($paneCount, 9)
    Write-Host "  タイムライン $paneCount 件、番号バッジ 1..$checkCount を検査" -ForegroundColor DarkGray

    foreach ($i in 1..$checkCount) {
        # GetNewClosure() は使わない。別モジュールスコープに束縛され、
        # スクリプト内の Test-PaneId が見えなくなる。
        # Test-Smoke はその場で実行するので $i はそのまま使える。
        Test-Smoke "番号バッジ $i (PaneNumber$i)" {
            Test-PaneId "PaneNumber$i"
        }
    }

    Test-Smoke "ペインの設定ボタン (PaneSettingsBtn)" {
        Test-PaneId "PaneSettingsBtn"
    }
}

# ── 4. メニュー ───────────────────────────────────────────────────────────────
Write-Section "メニュー"
winapp ui invoke "AppMenuBtn" -a $AppPid 2>$null | Out-Null
Start-Sleep -Milliseconds 1500

# メニュー項目はポップアップウィンドウに出る
Test-Smoke "設定メニュー項目 (AppSettingsMenuItem)"   { winapp ui wait-for "AppSettingsMenuItem" -a $AppPid -t 5000 }
Test-Smoke "タイムライン追加サブメニュー (AddTimelineSubMenu)" { winapp ui wait-for "AddTimelineSubMenu" -a $AppPid -t 5000 }

# ── 5. 設定ウィンドウ ─────────────────────────────────────────────────────────
Write-Section "設定ウィンドウ"
winapp ui invoke "AppSettingsMenuItem" -a $AppPid 2>$null | Out-Null
Start-Sleep -Seconds 3

Test-Smoke "設定ウィンドウが開く" {
    if (Get-SettingsWindow) { $global:LASTEXITCODE = 0 }
    else {
        Write-Output ("設定ウィンドウが開かない。現在のウィンドウ: " +
            ((Get-AppWindows | ForEach-Object { "'$($_.title)'($($_.className))" }) -join ', '))
        $global:LASTEXITCODE = 1
    }
}

$sw = Get-SettingsWindow
if ($sw) {
    winapp ui screenshot window -a $AppPid -w $sw.hwnd -o (Join-Path $ScreenshotDir "02-settings.png") 2>$null | Out-Null

    # NavigationView の各ページ（ContentDialog ではなく独立ウィンドウ）
    foreach ($nav in @(
        @{ Id = "NavGeneral";       Label = "全般" },
        @{ Id = "NavUserInterface"; Label = "ユーザーインターフェイス" },
        @{ Id = "NavExperimental";  Label = "試験機能" },
        @{ Id = "NavAbout";         Label = "バージョン情報" }
    )) {
        Test-Smoke "ナビゲーション項目 $($nav.Id) ($($nav.Label))" {
            winapp ui wait-for $nav.Id -a $AppPid -w $sw.hwnd -t 5000
        }
    }

    # ユーザーインターフェイスページのコントロール（ThemeCombo / LanguageCombo）
    winapp ui invoke "NavUserInterface" -a $AppPid -w $sw.hwnd 2>$null | Out-Null
    Start-Sleep -Seconds 2
    Test-Smoke "テーマ選択 (ThemeCombo)"   { winapp ui wait-for "ThemeCombo"    -a $AppPid -w $sw.hwnd -t 5000 }
    Test-Smoke "言語選択 (LanguageCombo)" { winapp ui wait-for "LanguageCombo" -a $AppPid -w $sw.hwnd -t 5000 }

    # バージョン情報ページ
    winapp ui invoke "NavAbout" -a $AppPid -w $sw.hwnd 2>$null | Out-Null
    Start-Sleep -Seconds 2
    winapp ui screenshot window -a $AppPid -w $sw.hwnd -o (Join-Path $ScreenshotDir "03-about.png") 2>$null | Out-Null

    # 後始末: 設定ウィンドウを閉じる
    winapp ui invoke "Close" -a $AppPid -w $sw.hwnd 2>$null | Out-Null
    Start-Sleep -Milliseconds 800
}

# ── 結果 ──────────────────────────────────────────────────────────────────────
Write-Host "`n────────────────────────────────"
Write-Host "PASS: $script:pass  FAIL: $script:fail"
if ($script:failures.Count -gt 0) {
    Write-Host "`n失敗した項目:" -ForegroundColor Red
    $script:failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
}
Write-Host "スクリーンショット: $ScreenshotDir"

if ($launched -and -not $KeepRunning) {
    Stop-Process -Id $AppPid -Force -ErrorAction SilentlyContinue
    Write-Host "アプリを終了しました (PID=$AppPid)"
}

exit ($(if ($script:fail -gt 0) { 1 } else { 0 }))
