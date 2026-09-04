using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace XTimelineViewer.Tests
{
    /// <summary>
    /// 拡張機能まわりの後始末を、ソースの文字列スキャンで固定する（#419）。
    ///
    /// MainWindow は拡張機能 1 つあたりの状態を複数の入れ物で手持ちしている。
    /// <b>アンインストールがそのうち 1 つでも取りこぼすと、入れ直したときに壊れる。</b>
    ///
    /// 実際に起きたのが #419。<c>_surfacedExtensionIds</c>（一覧へ出した印）だけ
    /// 掃除しておらず、同じ拡張機能を入れ直しても一覧にもツールバーにも出なくなった。
    /// 展開済み拡張機能の ID は Chromium がフォルダーの絶対パスから決めるので、
    /// 入れ直すと ID も同じになり、「もう出した」と判定され続けるため。
    ///
    /// ペイン側の同じ型の事故（#359 / #362）は TimelinePaneStructureTests で
    /// 固定してある。こちらは拡張機能側。
    /// テストは net8.0 で WinUI 型に触れないため、ソースを読んで照合する。
    /// </summary>
    public class ExtensionStructureTests
    {
        private static string FindRepoFile(string relative)
        {
            var rel = relative.Replace('/', Path.DirectorySeparatorChar);
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, rel);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException($"リポジトリ内で {relative} が見つかりません");
        }

        private static readonly string FieldsCs   = File.ReadAllText(FindRepoFile("Views/MainWindow.xaml.cs"));
        private static readonly string WebView2Cs = File.ReadAllText(FindRepoFile("Views/MainWindow.WebView2.cs"));

        /// <summary>
        /// アンインストールで片付けなくてよい入れ物と、その理由。
        /// <b>ここへ足すときは理由を書くこと。</b>「面倒だから」で足すと
        /// このテストが意味を失う。
        /// </summary>
        private static readonly string[] NotClearedOnUninstall =
        [
            // プロファイル単位の「読み込みパスを走らせたか」の記録。
            // 拡張機能を 1 つ消しても、そのプロファイルの読み込みが
            // 無かったことにはならない。消すと次のペイン初期化で
            // 読み込みが丸ごと走り直す。
            "_extensionsLoadedProfiles",
        ];

        /// <summary>波かっこを数えてメソッド本体を切り出す。</summary>
        private static string BodyOf(string source, string signature)
        {
            var at = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(at >= 0, $"{signature} が見つかりません");

            var open = source.IndexOf('{', at);
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
            }
            throw new InvalidOperationException($"{signature} の本体を閉じられません");
        }

        /// <summary>MainWindow が持つ「拡張機能ごとの入れ物」のフィールド名。</summary>
        private static string[] PerExtensionCollections()
            => Regex.Matches(FieldsCs,
                    @"private readonly (?:HashSet|Dictionary|List)<[^;]*?>\s+(_\w*[Ee]xtension\w*)")
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        [Fact]
        public void TheCollectionsAreFound()
        {
            // 正規表現が空振りしていたら、下のテストは何も見ていないことになる。
            var found = PerExtensionCollections();
            Assert.True(found.Length >= 4,
                "拡張機能ごとの入れ物を見つけられませんでした。フィールドの書き方が" +
                $"変わった可能性があります。見つかったもの: {string.Join(", ", found)}");
        }

        [Fact]
        public void Uninstall_CleansUpEveryPerExtensionCollection()
        {
            var body = BodyOf(WebView2Cs, "internal async Task<bool> UninstallExtensionAsync(");

            foreach (var field in PerExtensionCollections().Except(NotClearedOnUninstall, StringComparer.Ordinal))
            {
                Assert.True(body.Contains(field, StringComparison.Ordinal),
                    $"UninstallExtensionAsync が {field} に触れていません。" +
                    "拡張機能ごとの入れ物を足したら、アンインストールの後始末にも足してください。" +
                    "取りこぼすと、入れ直したときに壊れた形で残ります（#419）。" +
                    $"片付けないのが正しいなら {nameof(NotClearedOnUninstall)} へ理由付きで足してください。");
            }
        }

        [Fact]
        public void Uninstall_DropsTheSurfacedMarkBeforeTheIdIsUnavailable()
        {
            // ext.Id は登録を外した後には引けない。控えてから外すこと。
            var body = BodyOf(WebView2Cs, "internal async Task<bool> UninstallExtensionAsync(");

            var capture = body.IndexOf("surfacedIds", StringComparison.Ordinal);
            var remove  = body.IndexOf("RemoveAsync", StringComparison.Ordinal);

            Assert.True(capture >= 0, "外す前に ID を控えていません（#419）");
            Assert.True(capture < remove,
                "RemoveAsync の後で ext.Id を引こうとしています。登録を外した後では引けません。");
        }
    }
}
