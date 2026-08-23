using System;
using System.IO;
using System.Text.Json;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// 拡張機能の更新（#406）。新しい版があるかの判定。
    ///
    /// 落として置き換える段取りは <see cref="ExtensionInstallRunner"/> を使い回す。
    /// ここは<b>版を比べる規則</b>だけを持つ（IO と分けてテストするため）。
    /// </summary>
    internal static class ExtensionUpdater
    {
        /// <summary>
        /// 入っている拡張機能の版。<c>manifest.json</c> の <c>version</c>。
        /// 読めなければ null。
        /// </summary>
        internal static string? InstalledVersion(string extensionDir)
        {
            var path = Path.Combine(extensionDir, "manifest.json");
            if (!File.Exists(path)) return null;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
            }
            catch (Exception ex)
            {
                AppLog.Debug($"ExtensionUpdater: manifest を読めませんでした {extensionDir}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// リリース JSON からタグを取り出す。表示に使う。
        /// </summary>
        internal static string? TagOf(string releaseJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(releaseJson);
                return doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            }
            catch (Exception ex)
            {
                AppLog.Debug($"ExtensionUpdater: リリース JSON を読めませんでした: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 拡張機能の版を比べる。<paramref name="latest"/> のほうが新しければ true。
        ///
        /// Chrome の拡張機能の版は「1〜4 個の 0〜65535 をドットで繋いだもの」と決まっている。
        /// <see cref="Version"/> で素直に比べられるが、<b>タグは "v1.2.3" のように接頭辞が
        /// 付くことがある</b>ので落としてから比べる。
        ///
        /// どちらかが読めない形なら false（分からないものを「新しい」と言わない）。
        /// </summary>
        internal static bool IsNewer(string? installed, string? latest)
        {
            var a = ParseVersion(installed);
            var b = ParseVersion(latest);
            if (a is null || b is null) return false;
            return b > a;
        }

        private static Version? ParseVersion(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var t = text.Trim();
            if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];

            // "1.2.3-beta" のような接尾辞は落とす。比較できる部分だけ見る。
            var cut = t.IndexOfAny(['-', '+', ' ']);
            if (cut > 0) t = t[..cut];

            return Version.TryParse(t, out var v) ? v : null;
        }
    }
}
