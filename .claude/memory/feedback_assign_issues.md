---
name: feedback_assign_issues
description: イシュー作成時は必ず daruyanagi にアサインする
metadata:
  type: feedback
---

イシューを作成するときは常に `--assignee daruyanagi` を付ける。

**Why:** ユーザーの指示（2026-05-20）。

**How to apply:** `gh issue create` コマンドに毎回 `--assignee daruyanagi` を追加する。
