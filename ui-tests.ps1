# ui-tests.ps1 — XTimelineViewer UI Automation Tests
# Usage: .\ui-tests.ps1 -AppPid <PID>
#   AppPid: 実行中の XTimelineViewer のプロセス ID
#
# 事前準備:
#   1. XTimelineViewer をビルド・起動しておく
#   2. winapp がインストールされていること (winapp --version で確認)

param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Continue'
$pass = 0; $fail = 0; $results = @()

# メインウィンドウの HWND を取得（PopupHost を除外）
$windows = winapp ui list-windows -a $AppPid --json 2>$null | ConvertFrom-Json
$hwnd = ($windows | Where-Object { $_.title -ne "PopupHost" } | Select-Object -First 1).hwnd
if (-not $hwnd) {
    Write-Host "ERROR: XTimelineViewer (PID: $AppPid) が見つかりません" -ForegroundColor Red
    exit 1
}
Write-Host "テスト対象: PID=$AppPid, HWND=$hwnd"
New-Item -ItemType Directory -Force -Path "test-screenshots" | Out-Null

function Test-UI {
    param([string]$Name, [scriptblock]$Script)
    try {
        $output = & $Script 2>&1
        if ($LASTEXITCODE -eq 0) {
            $script:pass++
            $script:results += @{ name = $Name; status = "PASS" }
            Write-Host "  PASS: $Name" -ForegroundColor Green
        } else {
            $script:fail++
            $script:results += @{ name = $Name; status = "FAIL"; detail = "$output" }
            Write-Host "  FAIL: $Name — $output" -ForegroundColor Red
        }
    } catch {
        $script:fail++
        $script:results += @{ name = $Name; status = "FAIL"; detail = "$_" }
        Write-Host "  FAIL: $Name — $_" -ForegroundColor Red
    }
}

# ── スクリーンショット（初期状態） ─────────────────────────────────────────────
winapp ui screenshot -a $AppPid -o "test-screenshots/00-initial.png" 2>$null

# ── ツールバー要素の存在確認 ──────────────────────────────────────────────────
Write-Host "`n[ツールバー]"
Test-UI "投稿ボタン (PostBtn) が存在する" {
    winapp ui wait-for "PostBtn" -a $AppPid -t 3000
}
Test-UI "メニューボタン (AppMenuBtn) が存在する" {
    winapp ui wait-for "AppMenuBtn" -a $AppPid -t 3000
}

# ── メニュー操作 ───────────────────────────────────────────────────────────────
Write-Host "`n[メニュー]"
Test-UI "メニューボタンをクリックできる" {
    winapp ui invoke "AppMenuBtn" -a $AppPid
}
Start-Sleep -Milliseconds 500

winapp ui screenshot -a $AppPid -o "test-screenshots/01-menu-open.png" 2>$null

Test-UI "設定メニュー項目 (AppSettingsMenuItem) が表示される" {
    winapp ui wait-for "AppSettingsMenuItem" -a $AppPid -t 3000
}
Test-UI "About メニュー項目 (AboutMenuItem) が表示される" {
    winapp ui wait-for "AboutMenuItem" -a $AppPid -t 3000
}

# メニューを閉じる（Escape）
winapp ui key "Escape" -a $AppPid 2>$null
Start-Sleep -Milliseconds 300

# ── 設定ダイアログ ─────────────────────────────────────────────────────────────
Write-Host "`n[設定ダイアログ]"
Test-UI "メニューから設定を開ける" {
    winapp ui invoke "AppMenuBtn" -a $AppPid
    Start-Sleep -Milliseconds 400
    winapp ui invoke "AppSettingsMenuItem" -a $AppPid
}
Start-Sleep -Milliseconds 800
winapp ui screenshot -a $AppPid -o "test-screenshots/02-settings-dialog.png" 2>$null

Test-UI "設定ダイアログにキャンセルボタンがある" {
    winapp ui wait-for "Cancel" -a $AppPid -t 3000
}

# ダイアログを閉じる
winapp ui invoke "Cancel" -a $AppPid 2>$null
Start-Sleep -Milliseconds 500

# ── アクセシビリティ確認 ───────────────────────────────────────────────────────
Write-Host "`n[アクセシビリティ]"
Test-UI "PostBtn に AutomationProperties.Name が設定されている" {
    $prop = winapp ui get-property "PostBtn" -a $AppPid -p Name --json 2>$null | ConvertFrom-Json
    if ($prop.value -and $prop.value.Length -gt 0) { exit 0 } else { exit 1 }
}
Test-UI "AppMenuBtn に AutomationProperties.Name が設定されている" {
    $prop = winapp ui get-property "AppMenuBtn" -a $AppPid -p Name --json 2>$null | ConvertFrom-Json
    if ($prop.value -and $prop.value.Length -gt 0) { exit 0 } else { exit 1 }
}

# ── 最終スクリーンショット ─────────────────────────────────────────────────────
winapp ui screenshot -a $AppPid -o "test-screenshots/99-final.png" 2>$null

# ── 結果表示 ───────────────────────────────────────────────────────────────────
Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
Write-Host "結果: 成功 $pass / 失敗 $fail (合計 $($pass + $fail))"
$results | ConvertTo-Json | Out-File "test-results.json"

if ($fail -gt 0) {
    Write-Host "スクリーンショット: test-screenshots/ を確認してください"
    exit 1
} else {
    Write-Host "すべてのテストが成功しました！" -ForegroundColor Green
    exit 0
}
