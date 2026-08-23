using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// GitHub のリリースから拡張機能を入れる（#399）。
    ///
    /// Chrome Web ストアからの取得も検討したが採らなかった。WebView2 に
    /// ストアからインストールする API が無く、`.crx` の配信エンドポイントは
    /// 非公開で、Google の利用規約はプログラムからの取得を制限している。
    /// GitHub はリリース API が公開されているので正攻法で実装できる。
    ///
    /// 判断の部分は IO と分けてある（テストプロジェクトからリンクして検証するため）。
    /// </summary>
    internal static class ExtensionInstaller
    {
        /// <summary>入れる候補。</summary>
        internal sealed record Candidate(string Name, string DownloadUrl);

        // ── URL の読み取り ───────────────────────────────────────────────

        /// <summary>
        /// GitHub の URL から owner/repo を取り出す。
        /// リポジトリ・リリース一覧・特定のタグ、どの形でも受ける。
        /// </summary>
        internal static (string Owner, string Repo)? ParseRepoUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            var m = Regex.Match(
                url.Trim(),
                @"^(?:https?://)?(?:www\.)?github\.com/([A-Za-z0-9._-]+)/([A-Za-z0-9._-]+?)(?:\.git)?(?:/.*)?$",
                RegexOptions.IgnoreCase);

            if (!m.Success) return null;

            var repo = m.Groups[2].Value;
            // github.com/owner だけ、のような形は拒む
            if (repo.Length == 0) return null;

            return (m.Groups[1].Value, repo);
        }

        internal static string LatestReleaseApiFor(string owner, string repo)
            => $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

        // ── 資産の選択 ───────────────────────────────────────────────────

        /// <summary>
        /// リリース JSON から、拡張機能になりうる資産を挙げる。
        /// 複数あるときは呼び出し側で選ばせる。
        /// </summary>
        internal static IReadOnlyList<Candidate> SelectCandidates(string releaseJson)
        {
            var list = new List<Candidate>();

            using var doc = JsonDocument.Parse(releaseJson);
            if (!doc.RootElement.TryGetProperty("assets", out var assets)) return list;

            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url  = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (name is null || url is null) continue;

                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".crx", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new Candidate(name, url));
                }
            }

            return list;
        }

        // ── CRX の取り扱い ───────────────────────────────────────────────

        /// <summary>
        /// `.crx` は先頭に署名ヘッダーが付いた ZIP。ZIP が始まる位置を返す。
        /// CRX でなければ 0（そのまま ZIP として扱う）。
        /// 形が読めなければ -1。
        /// </summary>
        /// <remarks>
        /// CRX2: "Cr24" + version(4) + 公開鍵長(4) + 署名長(4) + 公開鍵 + 署名
        /// CRX3: "Cr24" + version(4) + ヘッダー長(4) + ヘッダー
        /// </remarks>
        internal static int ZipOffsetOf(byte[] bytes)
        {
            if (bytes.Length < 16) return -1;

            // ZIP そのもの（"PK\x03\x04"）
            if (bytes[0] == 0x50 && bytes[1] == 0x4B) return 0;

            if (bytes[0] != (byte)'C' || bytes[1] != (byte)'r' ||
                bytes[2] != (byte)'2' || bytes[3] != (byte)'4') return -1;

            var version = BitConverter.ToUInt32(bytes, 4);

            if (version == 2)
            {
                var keyLen = BitConverter.ToUInt32(bytes, 8);
                var sigLen = BitConverter.ToUInt32(bytes, 12);
                var offset = 16L + keyLen + sigLen;
                return offset > 0 && offset < bytes.Length ? (int)offset : -1;
            }

            if (version == 3)
            {
                var headerLen = BitConverter.ToUInt32(bytes, 8);
                var offset = 12L + headerLen;
                return offset > 0 && offset < bytes.Length ? (int)offset : -1;
            }

            return -1;
        }

        // ── 中身の検査 ───────────────────────────────────────────────────

        /// <summary>
        /// 展開した中から拡張機能の根（manifest.json のある場所）を探す。
        /// ZIP によっては 1 階層挟んでいることがある。見つからなければ null。
        /// </summary>
        internal static string? FindExtensionRoot(string extractedDir)
        {
            if (File.Exists(Path.Combine(extractedDir, "manifest.json"))) return extractedDir;

            // 1 階層だけ潜る。それ以上は拡張機能の体を成していないとみなす。
            foreach (var sub in Directory.GetDirectories(extractedDir))
                if (File.Exists(Path.Combine(sub, "manifest.json"))) return sub;

            return null;
        }

        /// <summary>
        /// manifest.json から、確認画面に出す情報を読む。
        ///
        /// <b>拡張機能は X のページ上で任意のコードを実行でき、Cookie や DOM に触れる。</b>
        /// 何を許すことになるのかを見せてから入れる。
        /// </summary>
        internal static (string Name, string Version, IReadOnlyList<string> Permissions)? ReadManifest(string extensionRoot)
        {
            var path = Path.Combine(extensionRoot, "manifest.json");
            if (!File.Exists(path)) return null;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;

                var name    = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";

                var perms = new List<string>();
                foreach (var key in new[] { "permissions", "host_permissions", "optional_permissions" })
                {
                    if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                    foreach (var item in arr.EnumerateArray())
                        if (item.GetString() is { } s) perms.Add(s);
                }

                // content_scripts の matches も、実質「どこで動くか」なので見せる
                if (root.TryGetProperty("content_scripts", out var cs) && cs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in cs.EnumerateArray())
                    {
                        if (!entry.TryGetProperty("matches", out var matches) ||
                            matches.ValueKind != JsonValueKind.Array) continue;
                        foreach (var item in matches.EnumerateArray())
                            if (item.GetString() is { } s) perms.Add(s);
                    }
                }

                return (name, version, perms.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList());
            }
            catch (Exception ex)
            {
                AppLog.Error($"ExtensionInstaller.ReadManifest({extensionRoot})", ex);
                return null;
            }
        }

        /// <summary>
        /// 入れ先のフォルダー名（#406）。<b>リポジトリ名から作り、版数を含めない。</b>
        ///
        /// 以前は資産名をそのまま使っていた（<c>uBlock0_1.73.0.chromium.zip</c> →
        /// <c>uBlock0_1.73.0.chromium</c>）。しかしそれだと更新のたびにフォルダー名が
        /// 変わり、<b>3 つ同時に壊れる</b>。
        ///
        /// <list type="bullet">
        /// <item>有効・無効の記録（#398）はフォルダー名が鍵なので引き継げない</item>
        /// <item><b>拡張機能 ID はパスから導出される</b>ので変わる。プロファイルから見ると
        ///       別の拡張機能になる（同じ中身を別名で置くと別 ID になることを実測で確認）</item>
        /// <item>旧いフォルダーの登録がプロファイルに残り続ける</item>
        /// </list>
        ///
        /// リポジトリ名から作れば更新しても変わらず、3 つとも起きない。
        /// パス区切りなどが混ざらないよう、使えない文字は落とす。
        /// </summary>
        internal static string FolderNameFor(string owner, string repo)
            => Sanitize($"{owner}-{repo}");

        /// <summary>
        /// 入れ先のフォルダー名（リポジトリが分からない場合）。
        /// 資産名から作るので版数が混ざりうる。GitHub 経由なら
        /// <see cref="FolderNameFor(string, string)"/> を使うこと。
        /// </summary>
        internal static string FolderNameForAsset(string assetName)
            => Sanitize(Path.GetFileNameWithoutExtension(assetName));

        private static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
            return cleaned.Length > 0 ? cleaned : "extension";
        }
    }
}
