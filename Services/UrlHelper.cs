using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTimelineViewer.Services
{
    /// <summary>URL 判定・解析の純粋ロジック（UI 非依存）。</summary>
    internal static class UrlHelper
    {
        internal static bool IsXUrl(string url) =>
            url.Contains("x.com",       StringComparison.OrdinalIgnoreCase) ||
            url.Contains("twitter.com", StringComparison.OrdinalIgnoreCase);

        internal static bool IsOnBaseUrl(string currentUrl, string baseUrl)
        {
            if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var cur))  return false;
            if (!Uri.TryCreate(baseUrl,    UriKind.Absolute, out var @base)) return false;
            return string.Equals(cur.Host,         @base.Host,         StringComparison.OrdinalIgnoreCase)
                && string.Equals(cur.AbsolutePath, @base.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// アカウント依存のリスト一覧 URL（https://x.com/&lt;handle&gt;/lists）かどうか。
        /// プロファイル切り替え時にハンドルを差し替える対象の判定に使う。
        /// </summary>
        internal static bool IsPerUserListsUrl(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out var u)
               && Regex.IsMatch(u.AbsolutePath, @"^/[^/]+/lists$");

        // グリフは Segoe Fluent Icons の私用領域(PUA)コードポイント。
        // 生の PUA 文字を直書きするとエンコーディング事故で欠落するため (#122)、
        // 必ず "\uXXXX" エスケープ表記で記述すること。
        internal static string GetTimelineGlyph(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
            var p = uri.AbsolutePath;
            if (p.StartsWith("/home"))                                return "\uE80F"; // Home
            if (p.StartsWith("/notifications"))                       return "\uE7E7"; // Bell
            if (p.StartsWith("/search") || p.StartsWith("/explore")) return "\uE71E"; // Search
            if (p == "/bookmarks" || p.StartsWith("/bookmarks/") ||
                p == "/i/bookmarks" || p.StartsWith("/i/bookmarks/")) return "\uE734"; // Bookmark
            if (p == "/i/lists" || p.StartsWith("/i/lists/") ||
                Regex.IsMatch(p, @"^/[^/]+/lists$"))                  return "\uE71D"; // BulletedList (lists index / individual)
            if (p.StartsWith("/messages"))                            return "\uE8BD"; // Chat
            if (Regex.IsMatch(p, @"^/[^/]+$"))                        return "\uE77B"; // Contact
            return "\uE774"; // Globe
        }

        private static bool IsProfilePath(string p) =>
            Regex.IsMatch(p, @"^/[A-Za-z0-9_]+$") &&
            !p.StartsWith("/home") && !p.StartsWith("/notifications") &&
            !p.StartsWith("/search") && !p.StartsWith("/explore") &&
            !p.StartsWith("/bookmarks") && !p.StartsWith("/messages") &&
            !p.StartsWith("/i/");

        internal static bool IsListHeaderApplicable(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            var p = uri.AbsolutePath;
            return p.StartsWith("/notifications") ||
                   p.StartsWith("/search")        ||
                   p.StartsWith("/explore")       ||
                   p == "/bookmarks" || p.StartsWith("/bookmarks/") ||
                   p == "/i/bookmarks" || p.StartsWith("/i/bookmarks/") ||
                   p.StartsWith("/i/lists/")      ||
                   IsProfilePath(p);
        }

        /// <summary>.url ショートカットファイルの行から URL を抽出する。</summary>
        internal static string? ParseUrlShortcut(IEnumerable<string> lines)
        {
            foreach (var line in lines)
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    return line[4..].Trim();
            return null;
        }
    }
}
