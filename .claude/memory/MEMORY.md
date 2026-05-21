# Memory Index

- [イシュー実装時は必ずブランチを切る](feedback_branch_per_issue.md) — Issue を指定してプランニング・実装に進むときは git checkout -b から始める
- [PowerShell で GitHub API に日本語を送る際のエンコード](feedback_powershell_github_api_encoding.md) — Invoke-RestMethod は文字化けする。WebClient + UTF-8 バイト列を使う。
- [リリースビルドは x64 と arm64 の両方を含める](feedback_release_builds.md) — GitHub リリース時は win-x64 と win-arm64 の両方をビルドしてアセットに添付する。
- [コードベースに言語文字列を埋め込まない](feedback_no_hardcoded_strings.md) — 多言語対応のため UI 文字列は .resw リソースファイルに書き、コードに直接埋め込まない。
- [WinUI 3 unpackaged モードでの言語切り替え](feedback_winui3_language_switching.md) — PrimaryLanguageOverride は MSIX パッケージ ID 必須。unpackaged では resw ファイル直接パースを使う。
- [イシューは daruyanagi にアサイン](feedback_assign_issues.md) — gh issue create に毎回 --assignee daruyanagi を付ける。
