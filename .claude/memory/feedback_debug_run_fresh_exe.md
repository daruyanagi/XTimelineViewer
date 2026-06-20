---
name: debug-run-fresh-exe
description: デバッグ実行時はビルドした exe と起動する exe を必ず一致させ、exe の更新時刻を検証してから起動する
metadata: 
  node_type: memory
  type: feedback
  originSessionId: b3ca3c76-d26c-4354-92b4-f11707c5396e
---

デバッグ実行（ビルド＆起動）するときは、**ビルドした exe と起動する exe を必ず一致させる**。手順は [[debug-run]] スキルに従う:
1. 起動前に既存インスタンスを `Stop-Process -Name XTimelineViewer -Force` で必ず終了する。
2. ビルド開始時刻を記録し、ビルド後に **exe の LastWriteTime がそれより新しいことを検証**してから起動する（「ビルド成功」でも再リンクされず古いままのことがある）。
3. パスを決め打ちで当てず、`bin/` 配下で最も新しい `XTimelineViewer.exe` を起動する。
4. ブランチ破棄・`git reset` 直後などコード変化が怪しいときは `bin/`・`obj/` を消してクリーンリビルドする。

**Why:** `-p:Platform=x64` 付きビルドは `bin/x64/Debug/...` に出力されるのに、Platform なしの旧ビルドが残した `bin/Debug/...` の**古い exe を起動**し続け、「main のはずがブランチの機能が見える」と長時間ハマってユーザーに大きく時間を浪費させた（重大ミスとして指摘された）。出力ツリーが 2 つ並存しうるのが罠。

**How to apply:** 「デバッグ実行」と言われたら必ず [[debug-run]] スキルの PowerShell スニペット（kill → 時刻記録 → build → 鮮度検証 → 単一起動）を使う。検証に失敗したら起動せず、bin/obj を消してクリーンリビルドする。
