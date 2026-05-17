---
name: イシュー実装時のブランチ運用
description: Issue の実装開始・完了時のブランチ操作ルール
type: feedback
originSessionId: 69aa5296-fff9-465c-a0e5-3ced266e0134
---
**開始時：** Issue を指定してプランニング・実装を行うときは、必ず最初にブランチを切ってから作業を開始する。`git checkout -b fix/issue-N-short-description` または `feat/issue-N-short-description`。

**完了時：** PR がマージされてブランチが削除された後は、必ず `git checkout main && git pull` で main に戻す。

**Why:** ユーザーの明示的な取り決め。main への直接コミットを避け、マージ後も作業ブランチに残留しないため。

**How to apply:** 実装開始前に checkout -b、PR マージ確認後に checkout main && pull を実行する。
