using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// WebView2 へ注入する JavaScript を読み込む。
    ///
    /// 以前は C# の生文字列リテラルに直接書いていた（`MainWindow.*` の 18%、約 750 行）。
    /// JS として壊れていても実行するまで分からず、構文強調も静的検査も効かなかった。
    /// `Scripts/*.js` に出して**埋め込みリソース**として持つ。
    ///
    /// Content（ファイルコピー）ではなく埋め込みにしたのは、ZIP 配布で
    /// ファイルが欠ける事故を避けるため。存在確認は `ScriptLoaderTests` が CI で行う。
    /// </summary>
    internal static class ScriptLoader
    {
        private static readonly Dictionary<string, string> Cache = [];
        private static readonly object Gate = new();

        /// <summary>
        /// `Scripts/&lt;name&gt;.js` を読む。見つからない場合は空文字を返し、記録する。
        /// 空を注入しても他の機能は動き続ける（その機能だけが無効になる）。
        /// </summary>
        internal static string Get(string name)
        {
            lock (Gate)
            {
                if (Cache.TryGetValue(name, out var cached)) return cached;
                var text = Read(typeof(ScriptLoader).Assembly, name);
                Cache[name] = text;
                return text;
            }
        }

        /// <summary>テストから任意のアセンブリを指定できるようにしたもの。</summary>
        internal static string Read(Assembly assembly, string name)
        {
            // 埋め込みリソース名は「既定の名前空間 + フォルダー + ファイル名」。
            // 前方一致で拾えば、名前空間の設定変更に引きずられない。
            var suffix = $"Scripts.{name}.js";
            var resource = assembly.GetManifestResourceNames()
                                   .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal));
            if (resource is null)
            {
                AppLog.Error("ScriptLoader", new FileNotFoundException(
                    $"埋め込みリソース '{suffix}' が見つかりません。Scripts/{name}.js が " +
                    "EmbeddedResource として含まれているか確認してください。"));
                return string.Empty;
            }

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>埋め込まれている JS の名前一覧（テスト用）。</summary>
        internal static IEnumerable<string> Names(Assembly assembly)
            => assembly.GetManifestResourceNames()
                       .Where(n => n.Contains(".Scripts.", StringComparison.Ordinal) &&
                                   n.EndsWith(".js", StringComparison.Ordinal))
                       .Select(n => n[(n.IndexOf(".Scripts.", StringComparison.Ordinal) + ".Scripts.".Length)..^".js".Length]);
    }
}
