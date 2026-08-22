using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace XTimelineViewer.Tests
{
    /// <summary>
    /// 例外の扱いの方針を固定する（#374）。
    ///
    /// - 待たない非同期処理は <c>FireAndForget(context)</c> を通すこと。
    ///   生の <c>_ = SomethingAsync()</c> は例外を誰も観測しない（#339 がこれ）。
    /// - 空の <c>catch</c> は「なぜ無音でよいか」をその場に書くこと。
    ///   握りつぶす判断そのものは妥当な場面が多いが、理由が無いと
    ///   次に読む人が「これは直すべきか？」で止まる。
    ///
    /// ユニットテストからは WinUI 型に触れないため（テストは net8.0）、
    /// KeyboardShortcutDriftTests と同じくソースを読んで照合する。
    /// </summary>
    public class ExceptionPolicyTests
    {
        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Views"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!;
        }

        /// <summary>本体の C# ソース（obj / bin / テストプロジェクトは除く）。</summary>
        private static IEnumerable<string> ProductionSources()
        {
            var root = RepoRoot();
            foreach (var sub in new[] { "Views", "Services", "Models", "ViewModels" })
            {
                var d = Path.Combine(root.FullName, sub);
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
                    yield return f;
            }
            foreach (var f in new[] { "App.xaml.cs", "R.cs", "PackageContext.cs" })
            {
                var p = Path.Combine(root.FullName, f);
                if (File.Exists(p)) yield return p;
            }
        }

        private static string Rel(string full) => Path.GetRelativePath(RepoRoot().FullName, full);

        [Fact]
        public void FireAndForget_IsUsedInsteadOfBareDiscard()
        {
            var offenders = new List<string>();
            foreach (var f in ProductionSources())
            {
                var lines = File.ReadAllLines(f);
                for (int i = 0; i < lines.Length; i++)
                    if (Regex.IsMatch(lines[i], @"^\s*_ = .*Async\("))
                        offenders.Add($"{Rel(f)}:{i + 1}  {lines[i].Trim()}");
            }
            Assert.True(offenders.Count == 0,
                "生の _ = ...Async() が残っています。FireAndForget(context) を使ってください:"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        [Fact]
        public void EmptyCatchBlocks_ExplainWhyTheyAreSilent()
        {
            var offenders = new List<string>();
            foreach (var f in ProductionSources())
            {
                var lines = File.ReadAllLines(f);
                var inRaw = RawStringFlags(lines);

                for (int i = 0; i < lines.Length; i++)
                {
                    // 注入 JS（生文字列リテラル）の中の catch は対象外
                    if (inRaw[i] || !Regex.IsMatch(lines[i], @"\bcatch\b")) continue;

                    var joined = string.Join(" ", lines.Skip(i).Take(3));
                    var m = Regex.Match(StripStringLiterals(joined), @"catch[^{]*\{(.*?)\}", RegexOptions.Singleline);
                    if (!m.Success) continue;

                    var inner = Regex.Replace(m.Groups[1].Value, @"/\*.*?\*/", "", RegexOptions.Singleline);
                    inner = Regex.Replace(inner, @"//.*", "").Trim();
                    if (inner.Length > 0) continue;                       // 何かしている catch は対象外

                    if (lines[i].Contains("/*") || lines[i].Contains("//")) continue;   // 理由が書いてある
                    offenders.Add($"{Rel(f)}:{i + 1}  {lines[i].Trim()}");
                }
            }
            Assert.True(offenders.Count == 0,
                "理由の書かれていない空の catch があります。なぜ無音でよいかをその場に書いてください:"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        /// <summary>各行が生文字列リテラル（""" ... """）の内側かどうか。</summary>
        private static bool[] RawStringFlags(string[] lines)
        {
            var flags = new bool[lines.Length];
            bool inRaw = false;
            for (int i = 0; i < lines.Length; i++)
            {
                int n = Regex.Matches(lines[i], "\"\"\"").Count;
                flags[i] = inRaw || n > 0;
                if (n % 2 == 1) inRaw = !inRaw;
            }
            return flags;
        }

        /// <summary>
        /// 通常の文字列リテラルの中身を落とす。埋め込まれた JS の波括弧を
        /// catch ブロックと誤認しないため。
        /// </summary>
        private static string StripStringLiterals(string code)
        {
            var sb = new StringBuilder(code.Length);
            bool inStr = false, esc = false;
            foreach (var c in code)
            {
                if (inStr)
                {
                    if (esc) { esc = false; continue; }
                    if (c == '\\') { esc = true; continue; }
                    if (c == '"') { inStr = false; sb.Append('"'); }
                    continue;
                }
                if (c == '"') { inStr = true; sb.Append('"'); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
