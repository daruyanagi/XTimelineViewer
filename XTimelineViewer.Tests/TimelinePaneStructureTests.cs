using System;
using System.IO;
using Xunit;

namespace XTimelineViewer.Tests
{
    /// <summary>
    /// タイムラインペインの状態管理まわりの構造を、ソースの文字列スキャンで固定する（#345）。
    ///
    /// MainWindow はペイン 1 つあたりの状態を複数の辞書で手持ちしており、ペインを消す経路が
    /// 2 つある（⚙ ダイアログの［削除］と、プロファイル削除による一括削除）。この 2 経路で
    /// 後始末の内容が食い違うと、実際にバグになる:
    ///
    ///   - #359 … 番号バッジの振り直し漏れ（表示と Ctrl+数字 の対応がずれる）
    ///   - #362 … 辞書 4 つの掃除漏れ（消えたペインへの参照が残る）
    ///   - #337 / #341 … バッジの列番号が 2 か所に手書きされ、片方だけ直した
    ///
    /// ユニットテストからは WinUI 型に触れないため（テストは net8.0）、
    /// KeyboardShortcutDriftTests と同じくソースを読んで照合する。
    /// </summary>
    public class TimelinePaneStructureTests
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

        private static readonly string MainWindowCs = File.ReadAllText(FindRepoFile("Views/MainWindow.xaml.cs"));
        // ⚙ ダイアログの［削除］がある側
        private static readonly string TimelineCs = File.ReadAllText(FindRepoFile("Views/MainWindow.Timeline.cs"));
        // プロファイル削除による一括削除がある側
        private static readonly string ProfilesCs = File.ReadAllText(FindRepoFile("Views/MainWindow.Profiles.cs"));

        /// <summary>
        /// ペイン単位の状態を MainWindow が辞書で手持ちしないこと。
        /// 以前は 7 つの辞書を手で同期しており、削除経路が 2 つあるせいで
        /// 後始末の抜けが実際にバグになっていた（#359 / #362）。
        /// #345 で全部 TimelinePane のフィールドへ畳んだので、戻らないよう固定する。
        /// </summary>
        [Fact]
        public void MainWindow_DoesNotHoldPerPaneDictionaries()
        {
            Assert.False(MainWindowCs.Contains("Dictionary<TimelinePane"),
                "MainWindow がペインをキーにした辞書を持っています。" +
                "削除経路が 2 つあるため、片方だけ掃除を忘れる事故が戻ります。" +
                "ペイン単位の状態は TimelinePane のフィールドに置いてください。");
        }


        [Fact]
        public void TimelineNumbers_AreRefreshedInBothDeletePaths()
        {
            const string token = "RefreshTimelineNumbers()";

            Assert.True(TimelineCs.Contains(token),
                $"⚙ ダイアログからの削除に '{token}' が見つかりません。");

            Assert.True(ProfilesCs.Contains(token),
                $"プロファイル削除に '{token}' が見つかりません。" +
                "番号バッジが表示順とずれます（#359 と同じ不具合）。");
        }

        // ── 以下は #345 のリファクタで達成した不変条件 ──

        [Fact]
        public void Profiles_DoesNotSearchVisualTreeByType()
        {
            foreach (var token in new[] { "OfType<Grid>()", "OfType<WebView2>()", "Grid.GetColumn" })
                Assert.False(ProfilesCs.Contains(token),
                    $"MainWindow.Profiles.cs に '{token}' が残っています。" +
                    "型や列番号で視覚ツリーを探すと、ペインの構造を変えた瞬間に無言で壊れます。");
        }

        /// <summary>
        /// 待たない非同期処理は FireAndForget を通すこと（#374）。
        /// 生の <c>_ = SomethingAsync()</c> は例外を誰も観測しない。
        /// #339 はまさにこれで、失敗が完全に無言だった。
        /// </summary>
        [Fact]
        public void FireAndForget_IsUsedInsteadOfBareDiscard()
        {
            var offenders = new System.Collections.Generic.List<string>();
            foreach (var rel in ViewSourceFiles())
            {
                var lines = File.ReadAllLines(FindRepoFile(rel));
                for (int i = 0; i < lines.Length; i++)
                    if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], @"^\s*_ = .*Async\("))
                        offenders.Add($"{rel}:{i + 1}  {lines[i].Trim()}");
            }
            Assert.True(offenders.Count == 0,
                "生の _ = ...Async() が残っています。FireAndForget(context) を使ってください:"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        private static System.Collections.Generic.IEnumerable<string> ViewSourceFiles()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "Views"))) root = root.Parent;
            Assert.NotNull(root);
            var viewsDir = Path.Combine(root!.FullName, "Views");
            foreach (var f in Directory.EnumerateFiles(viewsDir, "*.cs", SearchOption.AllDirectories))
                yield return "Views/" + Path.GetRelativePath(viewsDir, f).Replace(Path.DirectorySeparatorChar, '/');
            yield return "App.xaml.cs";
        }

        [Fact]
        public void HeaderColumns_AreDeclaredOnlyInXaml()
        {
            Assert.False(TimelineCs.Contains("Grid.SetColumn"),
                "MainWindow.Timeline.cs に 'Grid.SetColumn' が残っています。" +
                "ヘッダーの列番号は TimelinePane.xaml に一度だけ書かれているべきです（#337 の再発防止）。");
        }
    }
}
