---
name: WinUI 3 unpackaged モードでの言語切り替え
description: PrimaryLanguageOverride は MSIX パッケージ ID が必要。unpackaged 環境では resw ファイルを直接パースする。
type: feedback
originSessionId: 69aa5296-fff9-465c-a0e5-3ced266e0134
---
`Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride` は MSIX パッケージ ID が必要な WinRT API。unpackaged（デバッグ実行など）環境では `InvalidOperationException` が発生する。

**Why:** デバッグ時は MSIX としてデプロイされていないことが多く、`ApplicationData.Current` も同様に失敗する。

**How to apply:** 言語切り替えは以下の二段構えで実装する:
1. `PrimaryLanguageOverride` を try-catch で試みる（MSIX パッケージモード用）
2. `Strings/{lang}/Resources.resw` を `XDocument` で直接パースして辞書に保存し、`R.Get()` から優先的に返す（unpackaged モード用）
3. XAML の `x:Uid` は `PrimaryLanguageOverride` に依存するため、`x:Name` + コードビハインドで `R.Get()` を使う方式に変更する
