using System;
using System.Collections.Generic;
using System.Linq;

namespace XTimelineViewer.Services
{
    /// <summary>検索クエリ URL の構築・解析の純粋ロジック（UI 非依存）。</summary>
    internal static class SearchQueryHelper
    {
        internal static string BuildSearchUrl(string query)
        {
            var encoded = Uri.EscapeDataString(query);
            return $"https://x.com/search?q={encoded}&src=typed_query";
        }

        /// <summary>URL からクエリパス部分を抽出する（例: /search?q=test&amp;f=live）。src= は除外。</summary>
        internal static string? ExtractSearchPath(string? url)
        {
            if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            if (!uri.AbsolutePath.Equals("/search", StringComparison.OrdinalIgnoreCase)) return null;
            var qs = uri.Query;
            if (string.IsNullOrEmpty(qs)) return null;
            var meaningful = qs.TrimStart('?').Split('&')
                .Where(p => !p.StartsWith("src=", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return meaningful.Length > 0
                ? "/search?" + string.Join("&", meaningful)
                : null;
        }

        /// <summary>URL から q= パラメータの値を抽出する。</summary>
        internal static string? ExtractQueryFromUrl(string? url)
        {
            if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            var qs = uri.Query;
            if (string.IsNullOrEmpty(qs)) return null;
            foreach (var pair in qs.TrimStart('?').Split('&'))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && parts[0] == "q")
                    return Uri.UnescapeDataString(parts[1]);
            }
            return null;
        }

        /// <summary>クエリパスをデコードして表示用文字列を返す。</summary>
        internal static string DecodeSearchPath(string searchPath)
            => Uri.UnescapeDataString(searchPath);

        /// <summary>クエリパス（/search?q=...&amp;f=live）から q= の値を抽出する。</summary>
        internal static string? ExtractQueryFromSearchPath(string searchPath)
            => ExtractQueryFromUrl("https://x.com" + searchPath);

        /// <summary>保存済みクエリの先頭に追加する。既存の同一クエリは重複させず先頭へ移動する。</summary>
        internal static void AddSavedQuery(IList<string> saved, string searchPath)
        {
            saved.Remove(searchPath);
            saved.Insert(0, searchPath);
        }
    }
}
