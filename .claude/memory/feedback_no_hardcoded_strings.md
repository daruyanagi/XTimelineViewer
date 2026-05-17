---
name: コードベースに言語文字列を埋め込まない
description: 多言語対応のため、日本語や英語などの UI 文字列をコードに直接書かない
type: feedback
originSessionId: 69aa5296-fff9-465c-a0e5-3ced266e0134
---
今後は多言語対応を考慮し、コードベースに日本語（またはいかなる言語の UI 文字列も）を直接埋め込まない。

**Why:** issue #24 で多言語対応（日英）を実装した。今後も新機能・修正で文字列を追加するたびに `.resw` リソースファイルに書くことで、翻訳コストを最小化する。

**How to apply:**
- XAML に文字列を直接書く代わりに `x:Uid` を使い、`Strings/ja-JP/Resources.resw` と `Strings/en-US/Resources.resw` に登録する
- C# コードビハインドに文字列リテラルを書く代わりに `ResourceLoader.GetForViewIndependentUse().GetString("Key")` を使う
- 新しいキーを追加するときは ja-JP と en-US の両方に必ず追加する
