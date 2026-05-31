# ui-tests.ps1 — XTimelineViewer UI Automation Tests
# Usage: .\ui-tests.ps1 -AppPid <PID>
#   AppPid: 実行中の XTimelineViewer のプロセス ID
#
# 事前準備:
#   1. XTimelineViewer をビルド・起動しておく
#   2. winapp v0.3+ がインストールされていること: winget install Microsoft.WinAppCli

param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Continue'
$pass = 0; $fail = 0; $results = @()

# メインウィンドウの HWND を取得（PopupHost を除外）
$windows = (winapp ui list-windows -a $AppPid --json 2>$null) -join "" | ConvertFrom-Json
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
        $output = & $Script 2>&1          # 外部コマンドの exit code を保持するため Out-String より先に実行
        $ec     = $LASTEXITCODE           # パイプライン前に即キャプチャ
        $outputStr = $output -join "`n"
        if ($ec -eq 0) {
            $script:pass++
            $script:results += @{ name = $Name; status = "PASS" }
            Write-Host "  PASS: $Name" -ForegroundColor Green
        } else {
            $script:fail++
            $script:results += @{ name = $Name; status = "FAIL"; detail = $outputStr }
            Write-Host "  FAIL: $Name — $outputStr" -ForegroundColor Red
        }
    } catch {
        $script:fail++
        $script:results += @{ name = $Name; status = "FAIL"; detail = "$_" }
        Write-Host "  FAIL: $Name — $_" -ForegroundColor Red
    }
}

# ── 初期状態スクリーンショット ─────────────────────────────────────────────────
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
Start-Sleep -Milliseconds 600
winapp ui screenshot -a $AppPid -o "test-screenshots/01-menu-open.png" 2>$null

Test-UI "設定メニュー項目 (AppSettingsMenuItem) が表示される" {
    winapp ui wait-for "AppSettingsMenuItem" -a $AppPid -t 3000
}
Test-UI "About メニュー項目 (AboutMenuItem) が表示される" {
    winapp ui wait-for "AboutMenuItem" -a $AppPid -t 3000
}

# ── 設定ダイアログ ─────────────────────────────────────────────────────────────
Write-Host "`n[設定ダイアログ]"
# メニュー項目をクリックするとメニューが閉じてダイアログが開く
Test-UI "設定ダイアログを開ける" {
    winapp ui invoke "AppSettingsMenuItem" -a $AppPid
}
Start-Sleep -Milliseconds 800
winapp ui screenshot -a $AppPid -o "test-screenshots/02-settings-dialog.png" 2>$null

# ContentDialog のキャンセルボタンは AutomationId="CloseButton"（言語非依存）
Test-UI "設定ダイアログにキャンセルボタンがある" {
    winapp ui wait-for "CloseButton" -a $AppPid -t 3000
}
Test-UI "設定ダイアログに保存ボタンがある" {
    winapp ui wait-for "PrimaryButton" -a $AppPid -t 3000
}
Test-UI "設定ダイアログをキャンセルで閉じられる" {
    winapp ui invoke "CloseButton" -a $AppPid
}
Start-Sleep -Milliseconds 500

# ── アクセシビリティ確認 ───────────────────────────────────────────────────────
Write-Host "`n[アクセシビリティ]"
# AutomationId（x:Name から生成）で要素が発見できることを確認
Test-UI "PostBtn が AutomationId で発見できる" {
    winapp ui wait-for "PostBtn" -a $AppPid -t 3000
}
Test-UI "AppMenuBtn が AutomationId で発見できる" {
    winapp ui wait-for "AppMenuBtn" -a $AppPid -t 3000
}
# AutomationProperties.Name が設定されていることをプロパティ取得で確認
Test-UI "PostBtn に AutomationProperties.Name が設定されている（新規投稿）" {
    $out = winapp ui get-property "PostBtn" -a $AppPid --property Name 2>&1
    if (-not ($out -match "新規投稿|New Post")) { throw "Name プロパティ未設定。出力: $out" }
}

# ── About ダイアログ ──────────────────────────────────────────────────────────
Write-Host "`n[About ダイアログ]"
Test-UI "About ダイアログを開ける" {
    winapp ui invoke "AppMenuBtn" -a $AppPid
    Start-Sleep -Milliseconds 500
    winapp ui invoke "AboutMenuItem" -a $AppPid
}
Start-Sleep -Milliseconds 800
winapp ui screenshot -a $AppPid -o "test-screenshots/03-about-dialog.png" 2>$null

Test-UI "About ダイアログに閉じるボタンがある" {
    winapp ui wait-for "CloseButton" -a $AppPid -t 3000
}
Test-UI "About ダイアログを閉じられる" {
    winapp ui invoke "CloseButton" -a $AppPid
}
Start-Sleep -Milliseconds 500

# ── テーマ切り替え ─────────────────────────────────────────────────────────────
Write-Host "`n[テーマ切り替え]"

# ComboBox 内の指定テキストのアイテムスラグを返すヘルパー
function Get-ComboItem {
    param([string]$ComboId, [string]$ItemText, [int]$TargetPid)
    $out = winapp ui inspect $ComboId -a $TargetPid 2>$null
    ($out | Where-Object { $_ -match "ListItem.*$ItemText" } | Select-Object -First 1).Trim().Split(" ")[0]
}

Test-UI "テーマを Dark に変更して保存できる" {
    winapp ui invoke "AppMenuBtn" -a $AppPid
    Start-Sleep -Milliseconds 500
    winapp ui invoke "AppSettingsMenuItem" -a $AppPid
    Start-Sleep -Milliseconds 800
    winapp ui invoke "ThemeComboBox" -a $AppPid   # 展開
    Start-Sleep -Milliseconds 400
    $slug = Get-ComboItem -ComboId "ThemeComboBox" -ItemText "ダーク" -TargetPid $AppPid
    if (-not $slug) { throw "ダーク アイテムが見つからない" }
    winapp ui invoke $slug -a $AppPid
    Start-Sleep -Milliseconds 200
    winapp ui invoke "PrimaryButton" -a $AppPid
}
Start-Sleep -Milliseconds 600
winapp ui screenshot -a $AppPid -o "test-screenshots/04-dark-theme.png" 2>$null

Test-UI "テーマをシステムに戻して保存できる" {
    winapp ui invoke "AppMenuBtn" -a $AppPid
    Start-Sleep -Milliseconds 500
    winapp ui invoke "AppSettingsMenuItem" -a $AppPid
    Start-Sleep -Milliseconds 800
    winapp ui invoke "ThemeComboBox" -a $AppPid
    Start-Sleep -Milliseconds 400
    $slug = Get-ComboItem -ComboId "ThemeComboBox" -ItemText "システム" -TargetPid $AppPid
    if (-not $slug) { throw "システム アイテムが見つからない" }
    winapp ui invoke $slug -a $AppPid
    Start-Sleep -Milliseconds 200
    winapp ui invoke "PrimaryButton" -a $AppPid
}
Start-Sleep -Milliseconds 600
winapp ui screenshot -a $AppPid -o "test-screenshots/05-system-theme.png" 2>$null

# ── 言語切り替え ───────────────────────────────────────────────────────────────
Write-Host "`n[言語切り替え]"

Test-UI "言語を English に変更して保存できる" {
    winapp ui invoke "AppMenuBtn" -a $AppPid
    Start-Sleep -Milliseconds 500
    winapp ui invoke "AppSettingsMenuItem" -a $AppPid
    Start-Sleep -Milliseconds 800
    winapp ui invoke "LanguageComboBox" -a $AppPid
    Start-Sleep -Milliseconds 400
    $slug = Get-ComboItem -ComboId "LanguageComboBox" -ItemText "English" -TargetPid $AppPid
    if (-not $slug) { throw "English アイテムが見つからない" }
    winapp ui invoke $slug -a $AppPid
    Start-Sleep -Milliseconds 200
    winapp ui invoke "PrimaryButton" -a $AppPid
}
Start-Sleep -Milliseconds 800

Test-UI "言語変更後に再起動通知ダイアログが表示される" {
    winapp ui wait-for "CloseButton" -a $AppPid -t 3000
}
Test-UI "再起動通知を閉じられる" {
    winapp ui invoke "CloseButton" -a $AppPid
}
Start-Sleep -Milliseconds 500

Test-UI "言語をシステムに戻して保存できる" {
    winapp ui invoke "AppMenuBtn" -a $AppPid
    Start-Sleep -Milliseconds 500
    winapp ui invoke "AppSettingsMenuItem" -a $AppPid
    Start-Sleep -Milliseconds 800
    winapp ui invoke "LanguageComboBox" -a $AppPid
    Start-Sleep -Milliseconds 400
    $slug = Get-ComboItem -ComboId "LanguageComboBox" -ItemText "システム言語" -TargetPid $AppPid
    if (-not $slug) { throw "システム言語 アイテムが見つからない" }
    winapp ui invoke $slug -a $AppPid
    Start-Sleep -Milliseconds 200
    winapp ui invoke "PrimaryButton" -a $AppPid
}
Start-Sleep -Milliseconds 800

Test-UI "言語戻し後の再起動通知を閉じられる" {
    winapp ui wait-for "CloseButton" -a $AppPid -t 3000
    winapp ui invoke "CloseButton" -a $AppPid
}
Start-Sleep -Milliseconds 500

# ── 最終スクリーンショット ─────────────────────────────────────────────────────
winapp ui screenshot -a $AppPid -o "test-screenshots/99-final.png" 2>$null

# ── 結果 ───────────────────────────────────────────────────────────────────────
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
