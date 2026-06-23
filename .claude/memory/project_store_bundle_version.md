---
name: store-bundle-version-date-based
description: Microsoft Store のパートナーセンターに出る .msixbundle の版番号は日付ベース（2026.MMDD.HHMM.0）で正常。中身のアプリは正しい x.y.z.0
metadata: 
  node_type: memory
  type: project
  originSessionId: b3ca3c76-d26c-4354-92b4-f11707c5396e
---

`Release.ps1` が作る `.msixbundle` を Partner Center にアップすると、版番号が **`YYYY.MMDD.HHMM.0`（例 `2026.621.143.0`）, Neutral** と表示されるが、**これは正常**。

- 表示されているのは**バンドル（コンテナ）の Identity バージョン**。Release.ps1 の `MakeAppx bundle` が `/bv` を指定しないため、MakeAppx がビルド日時から自動採番する。
- **中身のアプリパッケージは正しく `x.y.z.0`**（x64/arm64）で、ユーザーに見えるアプリのバージョンはこちら。GitHub リリース（`vX.Y.Z`）/winget と一致する。
- 日付ベースは常に増加するので Store は毎回受理する。**セマンティック版（例 1.7.0.0）に揃えようとしてはいけない**——Store には既に日付ベースの大きい版番号が登録済みで、`1.7.0.0 < 2026.x` となり**拒否される**。日付ベースのまま運用するのが正解。

確認方法: バンドル内 `AppxMetadata/AppxBundleManifest.xml` の `<Identity Version>` がバンドル版、各 `<Package Version>` がアプリ版。リリース手順は [[x64-arm64]] と release スキル参照。
