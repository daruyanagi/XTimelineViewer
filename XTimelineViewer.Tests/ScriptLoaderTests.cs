using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace XTimelineViewer.Tests
{
    /// <summary>
    /// WebView2 へ注入する JS（#345）。
    ///
    /// 以前は C# の生文字列リテラルに直書きしていた。`Scripts/*.js` へ出したことで
    /// 構文強調と静的検査が効くようになったが、代わりに
    /// <b>「埋め込み忘れ」で無言に機能が消える</b>という失敗のしかたが生まれた。
    /// 実行時は空文字を返して他の機能を巻き込まない設計にしてあるので、
    /// 欠落はここ（CI）で捕まえる。
    ///
    /// テストプロジェクトは本体アセンブリを参照していないため、
    /// リポジトリ上のファイルと csproj の登録を直接照合する。
    /// </summary>
    public class ScriptLoaderTests
    {
        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!;
        }

        /// <summary>C# 側が ScriptLoader.Get("...") で要求している名前。</summary>
        private static IEnumerable<string> RequestedNames()
        {
            var root = RepoRoot();
            foreach (var f in Directory.EnumerateFiles(Path.Combine(root.FullName, "Views"), "*.cs", SearchOption.AllDirectories))
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), @"ScriptLoader\.Get\(""([^""]+)""\)"))
                    yield return m.Groups[1].Value;
        }

        [Fact]
        public void EveryRequestedScript_HasAFile()
        {
            var root = RepoRoot();
            var missing = RequestedNames()
                .Distinct()
                .Where(n => !File.Exists(Path.Combine(root.FullName, "Scripts", n + ".js")))
                .ToList();

            Assert.True(missing.Count == 0,
                "ScriptLoader.Get が要求しているのに Scripts/ に無い JS があります: " + string.Join(", ", missing));
        }

        [Fact]
        public void EveryScriptFile_IsRequestedFromCode()
        {
            var root = RepoRoot();
            var requested = RequestedNames().Distinct().ToHashSet();
            var orphans = Directory.EnumerateFiles(Path.Combine(root.FullName, "Scripts"), "*.js")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !requested.Contains(n!))
                .ToList();

            Assert.True(orphans.Count == 0,
                "どこからも読まれていない JS があります。使わないなら消してください: " + string.Join(", ", orphans!));
        }

        [Fact]
        public void ScriptsAreRegisteredAsEmbeddedResource()
        {
            // ここが抜けると、ビルドは通るのに実行時だけ機能が消える。
            var csproj = File.ReadAllText(Path.Combine(RepoRoot().FullName, "XTimelineViewer.csproj"));
            Assert.Contains("EmbeddedResource", csproj);
            Assert.Contains("Scripts/*.js", csproj);
        }

        [Theory]
        [InlineData("KeyboardShortcut",   "chrome.webview.postMessage")]
        [InlineData("TimestampIntercept", "openTimestamp:")]
        [InlineData("EditStateReporter",  "editing:")]
        [InlineData("MediaOverlayButton", "xtv-enlarge-btn")]
        [InlineData("PriorRepostSearch",  "searchPriorRepost:")]
        [InlineData("HomeAutoLoad",       "homeAutoLoad:")]
        public void Script_IsNotTruncated(string name, string sentinel)
        {
            // 切り出しでファイルが尻切れになっていないかを、要の 1 語で確かめる
            var text = File.ReadAllText(Path.Combine(RepoRoot().FullName, "Scripts", name + ".js"));
            Assert.Contains(sentinel, text);
        }

        [Theory]
        [InlineData("KeyboardShortcut")]
        [InlineData("TimestampIntercept")]
        [InlineData("EditStateReporter")]
        [InlineData("MediaOverlayButton")]
        [InlineData("PriorRepostSearch")]
        [InlineData("HomeAutoLoad")]
        public void Script_HasBalancedBraces(string name)
        {
            // 生文字列から切り出したときの取りこぼしを検出する簡易チェック。
            // 文字列・コメント・正規表現の中は数えない。
            var text = File.ReadAllText(Path.Combine(RepoRoot().FullName, "Scripts", name + ".js"));
            Assert.Equal(0, BraceBalance(text));
        }

        private static int BraceBalance(string js)
        {
            int depth = 0;
            bool inS = false, inD = false, inTpl = false, inLine = false, inBlock = false, esc = false;
            for (int i = 0; i < js.Length; i++)
            {
                char c = js[i];
                char next = i + 1 < js.Length ? js[i + 1] : '\0';

                if (inLine) { if (c == '\n') inLine = false; continue; }
                if (inBlock) { if (c == '*' && next == '/') { inBlock = false; i++; } continue; }
                if (inS || inD || inTpl)
                {
                    if (esc) { esc = false; continue; }
                    if (c == '\\') { esc = true; continue; }
                    if (inS && c == '\'') inS = false;
                    else if (inD && c == '"') inD = false;
                    else if (inTpl && c == '`') inTpl = false;
                    continue;
                }
                if (c == '/' && next == '/') { inLine = true; i++; continue; }
                if (c == '/' && next == '*') { inBlock = true; i++; continue; }
                if (c == '\'') { inS = true; continue; }
                if (c == '"') { inD = true; continue; }
                if (c == '`') { inTpl = true; continue; }
                if (c == '{') depth++;
                if (c == '}') depth--;
            }
            return depth;
        }
    }
}
