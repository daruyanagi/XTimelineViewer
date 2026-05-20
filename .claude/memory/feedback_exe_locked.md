---
name: feedback-exe-locked
description: EXE がロックされてビルドできない場合はユーザーに伝えてアプリを終了してもらう
metadata: 
  node_type: memory
  type: feedback
  originSessionId: b84ba1c0-4a56-4a95-b443-283b8d13ae7d
---

ビルド時に EXE がロックされている場合（別プロセスが使用中）、自分でプロセスをkillしようとしない。

**Why:** ユーザーが自分でアプリを終了したい。

**How to apply:** ビルドエラーログに「XTimelineViewer.exe」がロックされていると出たら、「アプリが起動中でビルドできません。閉じてください」と伝えて待つ。

デバッグ実行は常に `dotnet run`（`--no-build` なし）で行う。`--no-build` は古いバイナリを実行するリスクがある。
