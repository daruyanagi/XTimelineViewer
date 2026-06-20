---
name: debug-run
description: XTimelineViewer をデバッグ実行（ビルド＆起動）する頑健な手順。ユーザーが「デバッグ実行」「動かして」「起動して確認」等と言ったときに使う。古い exe を起動して時間を浪費する事故を防ぐ。
---

# XTimelineViewer デバッグ実行（頑健版）

## なぜ厳密にやるか（過去の事故）
`-p:Platform=x64` 付きビルドは **`bin/x64/Debug/...`** に出力されるが、Platform なしの旧ビルドは **`bin/Debug/...`** に出力される。出力ツリーが 2 つ並存している状態で、ビルド先と違う方の **古い exe を起動**してしまい、「main のはずなのにブランチの機能が見える」と長時間ハマったことがある。**ビルドした exe と起動する exe を必ず一致させる。**

## 鉄則
1. **起動前に既存インスタンスを必ず終了**（ファイルロックで exe が再リンクされず古いまま残る／複数ウィンドウでどれを見ているか分からなくなる、を防ぐ）。
2. **ビルド成功 ≠ exe 更新**。インクリメンタルビルドは「成功」と出ても再リンクしないことがある。**exe の LastWriteTime がビルド開始時刻より新しいことを必ず検証**してから起動する。
3. **起動するのは「いま生成された exe」**。パスを決め打ちで当てない。検証に失敗したら起動せず原因を調べる。
4. ブランチ切替・`git reset` の直後など、**コードが変わったか怪しいときは `bin/` `obj/` を消してクリーンリビルド**する。

## 手順（コピペ用）
ビルド開始時刻を記録 → ビルド → 生成 exe の鮮度を検証 → 起動、を 1 つの PowerShell で行う。

```powershell
# 1) 既存インスタンスを終了
Stop-Process -Name XTimelineViewer -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

# 2) ビルド開始時刻を記録してビルド（Platform=x64 を常用）
$start = Get-Date
dotnet build "C:\Users\hideto\source\repos\XTimelineViewer\XTimelineViewer.csproj" `
    -c Debug -p:Platform=x64 --nologo -v q

# 3) 生成された exe を探し、ビルド開始より新しいことを検証
$exe = Get-ChildItem "C:\Users\hideto\source\repos\XTimelineViewer\bin" -Recurse -Filter XTimelineViewer.exe |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe)              { throw "exe が見つからない。ビルド失敗の可能性" }
if ($exe.LastWriteTime -lt $start) {
    throw "exe が古い（$($exe.LastWriteTime)）。再リンクされていない。bin/obj を消してクリーンリビルドせよ"
}

# 4) 単一インスタンスで起動
"起動: $($exe.FullName)  ($($exe.LastWriteTime))"
Start-Process $exe.FullName
```

## クリーンリビルドが必要なとき
ブランチ破棄・`git reset --hard`・出力ツリーが複数並存している疑いがあるとき:
```powershell
Stop-Process -Name XTimelineViewer -Force -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "C:\Users\hideto\source\repos\XTimelineViewer\bin",`
    "C:\Users\hideto\source\repos\XTimelineViewer\obj" -ErrorAction SilentlyContinue
# その後、上記の手順でビルド＆起動
```

## 確認後
- UI の目視確認はユーザーが行う（computer-use は使わない）。ビルド＆起動までがエージェントの役割。
- 複数のデバッグウィンドウを残さない（次の検証の混乱の元）。
