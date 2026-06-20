---
name: ui-string
description: XTimelineViewer に UI 文字列（ラベル・ツールチップ・メッセージ等）を追加・変更するときの約束事。多言語リソース（.resw）とアイコングリフの扱い。新機能や修正で表示テキストを追加・変更するときに使う。
---

# UI 文字列の追加・変更

UI 文字列はコードに直接書かず、必ず多言語リソースに登録する（日英対応のため）。

## 文字列リソース

- 追加先は **2 ファイル両方**:
  - `Strings/ja-JP/Resources.resw`（日本語）
  - `Strings/en-US/Resources.resw`（英語）
  - **片方だけにすると言語切り替えで空欄になる。必ず両方に同じキーを追加する。**
- 形式:
  ```xml
  <data name="My_Key" xml:space="preserve"><value>テキスト</value></data>
  ```
- 関連するキーはセクションコメント（`<!-- ... -->`）の近くにまとめる。

## コードからの取得

- C# / コードビハインドからは **`R.Get("My_Key")`** で取得する（#198 で MRT Core ベースの `R` クラスに統一済み）。
  - `ResourceLoader.GetForViewIndependentUse(...)` は使わない。
- XAML の静的ラベルは `x:Uid` も使用可能（#198 以降）。`x:Uid` 形式のドット入りキー（例 `PostLabel.Text`）は PRI 内で `PostLabel/Text` として格納され、`R.Get` 側で `.`→`/` 変換して解決している。
- 言語切り替え（#117/#198）の即時反映のため、常駐 UI のテキストは `RefreshUIText()` 等で再適用する設計になっている。設定ページは言語変更時に `PopulateUI()` を再実行して追従する。

## アイコングリフ（重要な落とし穴 #122）

Segoe Fluent Icons の私用領域（PUA）グリフは、**必ず `\uXXXX` エスケープ表記で書く。生の PUA 文字を直書きしない。**

```csharp
Glyph = "\uE80F"   // OK: \uXXXX エスケープ表記（Home アイコン）
// 生の PUA 文字を直書きするのは NG（保存時のエンコーディング事故で欠落する）
```

- エディタやリンターが生 PUA 文字を勝手に挿入/変換することがある。編集後はグリフが `\uXXXX` のままか必ず確認する（このスキルを書く際も一度、生文字が混入した）。
- URL からアイコンを導出する箇所は `UrlHelper.GetTimelineGlyph(url)` に集約されている。新しい URL 種別を足すときはここに分岐を追加し、テスト（`UrlHelperTests`）も更新する。テストの期待値も `\uXXXX` で書く。

## チェックリスト
- [ ] ja-JP と en-US の両方にキーを追加した
- [ ] コードは `R.Get("Key")` で取得している
- [ ] グリフは `\uXXXX` エスケープになっている
