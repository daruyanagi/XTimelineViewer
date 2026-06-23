# Memory Index

- [イシュー実装時は必ずブランチを切る](feedback_branch_per_issue.md) — Issue を指定してプランニング・実装に進むときは git checkout -b から始める
- [PowerShell で GitHub API に日本語を送る際のエンコード](feedback_powershell_github_api_encoding.md) — Invoke-RestMethod は文字化けする。WebClient + UTF-8 バイト列を使う。
- [リリースビルドは x64 と arm64 の両方を含める](feedback_release_builds.md) — GitHub リリース時は win-x64 と win-arm64 の両方をビルドしてアセットに添付する。
- [コードベースに言語文字列を埋め込まない](feedback_no_hardcoded_strings.md) — 多言語対応のため UI 文字列は .resw リソースファイルに書き、コードに直接埋め込まない。
- [WinUI 3 unpackaged モードでの言語切り替え](feedback_winui3_language_switching.md) — PrimaryLanguageOverride は MSIX パッケージ ID 必須。unpackaged では resw ファイル直接パースを使う。
- [イシューは daruyanagi にアサイン](feedback_assign_issues.md) — gh issue create に毎回 --assignee daruyanagi を付ける。
- [イシュー解決前にコメントをすべて読む](feedback_read_issue_comments.md) — 実装前に gh issue view <number> --comments でコメントまで確認する。本文だけ読んで着手しない。
- [デバッグ実行は生成直後の exe を起動](feedback_debug_run_fresh_exe.md) — ビルドした exe と起動する exe を一致させる。更新時刻を検証。debug-run スキルに従う。
- [Store のバンドル版番号は日付ベースで正常](project_store_bundle_version.md) — Partner Center の 2026.MMDD.HHMM.0 表示はバンドル版。中身は正しい x.y.z.0。セマンティック版に揃えない。
