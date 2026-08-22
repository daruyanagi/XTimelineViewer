using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace XTimelineViewer.Tests
{
    /// <summary>
    /// UI 文字列リソースの整合性。
    ///
    /// CLAUDE.md の約束「ja-JP と en-US のキーは常に一致させる」を機械で守る。
    /// 片方にだけ足すと、言語を切り替えたときにその場所が空欄になる。
    /// ビルドは通ってしまうので、気づくのは実行して切り替えたときだけだった。
    ///
    /// <see cref="Resw_IsNotEmpty"/> は、resw を書き換えるスクリプトが
    /// ファイルを空にしてしまった事故を受けて足したもの。「短くなったか」だけを
    /// 見ていると、全消しが「成功」として通ってしまう。
    ///
    /// テストは net8.0 で WinUI に触れないため、ファイルを直接読んで照合する
    /// （KeyboardShortcutDriftTests と同じ流儀）。
    /// </summary>
    public class ResourceParityTests
    {
        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Strings"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!;
        }

        private static string ReswPath(string lang)
            => Path.Combine(RepoRoot().FullName, "Strings", lang, "Resources.resw");

        private static List<string> Keys(string lang)
            => XDocument.Load(ReswPath(lang))
                        .Root!.Elements("data")
                        .Select(e => e.Attribute("name")!.Value)
                        .ToList();

        [Theory]
        [InlineData("ja-JP")]
        [InlineData("en-US")]
        public void Resw_IsNotEmpty(string lang)
        {
            // 全消しを「差分が小さい」と見逃さないための下限。
            // 目安であって上限ではないので、キーを増やしても落ちない。
            var keys = Keys(lang);
            Assert.True(keys.Count > 100,
                $"{lang} の Resources.resw のキーが {keys.Count} 件しかありません。壊れていませんか？");
        }

        [Fact]
        public void BothLanguages_HaveTheSameKeys()
        {
            var ja = Keys("ja-JP").ToHashSet();
            var en = Keys("en-US").ToHashSet();

            var onlyJa = ja.Except(en).OrderBy(k => k).ToList();
            var onlyEn = en.Except(ja).OrderBy(k => k).ToList();

            Assert.True(onlyJa.Count == 0 && onlyEn.Count == 0,
                "ja-JP と en-US でキーが食い違っています。" + Environment.NewLine
                + $"ja-JP にのみ: {string.Join(", ", onlyJa)}" + Environment.NewLine
                + $"en-US にのみ: {string.Join(", ", onlyEn)}");
        }

        [Theory]
        [InlineData("ja-JP")]
        [InlineData("en-US")]
        public void Resw_HasNoDuplicateKeys(string lang)
        {
            var dupes = Keys(lang).GroupBy(k => k)
                                  .Where(g => g.Count() > 1)
                                  .Select(g => g.Key)
                                  .ToList();
            Assert.True(dupes.Count == 0,
                $"{lang} に重複キーがあります: {string.Join(", ", dupes)}");
        }

        [Theory]
        [InlineData("ja-JP")]
        [InlineData("en-US")]
        public void Resw_HasNoEmptyValues(string lang)
        {
            var empty = XDocument.Load(ReswPath(lang))
                                 .Root!.Elements("data")
                                 .Where(e => string.IsNullOrWhiteSpace(e.Element("value")?.Value))
                                 .Select(e => e.Attribute("name")!.Value)
                                 .ToList();
            Assert.True(empty.Count == 0,
                $"{lang} に値が空のキーがあります: {string.Join(", ", empty)}");
        }

        [Fact]
        public void EveryRequestedKey_Exists()
        {
            // R.Get("...") で要求しているのに resw に無いと、実行時にその場所だけ空になる。
            var root = RepoRoot();
            var keys = Keys("ja-JP").ToHashSet();

            var missing = new List<string>();
            foreach (var sub in new[] { "Views", "Services", "Models", "ViewModels" })
            {
                var d = Path.Combine(root.FullName, sub);
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
                    foreach (Match m in Regex.Matches(File.ReadAllText(f), @"R\.Get\(""([^""]+)""\)"))
                    {
                        var key = m.Groups[1].Value;
                        // x:Uid 形式のドット入りキーは PRI 側で解決されるため対象外
                        if (key.Contains('.')) continue;
                        if (!keys.Contains(key))
                            missing.Add($"{Path.GetRelativePath(root.FullName, f)}: {key}");
                    }
            }

            Assert.True(missing.Count == 0,
                "R.Get が要求しているのに resw に無いキーがあります:" + Environment.NewLine
                + string.Join(Environment.NewLine, missing.Distinct()));
        }
    }
}
