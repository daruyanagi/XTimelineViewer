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

        // ⚙ ダイアログの［削除］がある側
        private static readonly string TimelineCs = File.ReadAllText(FindRepoFile("Views/MainWindow.Timeline.cs"));
        // プロファイル削除による一括削除がある側
        private static readonly string ProfilesCs = File.ReadAllText(FindRepoFile("Views/MainWindow.Profiles.cs"));

        /// <summary>
        /// ペイン 1 つあたり MainWindow が持っている状態。ペインを消すときは
        /// どちらの経路でも全部を掃除しなければならない。
        /// 新しくペイン単位の辞書を足したら、ここにも足すこと。
        /// </summary>
        public static TheoryData<string> PerPaneState() => new()
        {
            "_paneToSetFocus",
            "_paneUrlUpdaters",
            "_paneToConfig",
            "_autoLoadIndicators",
            "_paneNumberLabels",
            "_headerGridToPane",
            "_headerRefreshers",
        };

        [Theory]
        [MemberData(nameof(PerPaneState))]
        public void PaneState_IsCleanedUpInBothDeletePaths(string field)
        {
            var token = field + ".Remove(";

            Assert.True(TimelineCs.Contains(token),
                $"{field}: ⚙ ダイアログからの削除（MainWindow.Timeline.cs）に '{token}' が見つかりません。");

            Assert.True(ProfilesCs.Contains(token),
                $"{field}: プロファイル削除（MainWindow.Profiles.cs）に '{token}' が見つかりません。" +
                "消えたペインへの参照が残ります（#362 と同じ不具合）。");
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

        // ── ここから下は #345 段階 2B（TimelinePane の UserControl 化）の到達目標 ──
        // 現時点では成り立たないので Skip しておき、達成したら Skip を外す。
        // 上の 2 つのテストは、辞書そのものが無くなる段階 2B で不要になる。

        [Fact(Skip = "#345 段階 2B で TimelinePane に移すまで成り立たない")]
        public void Profiles_DoesNotSearchVisualTreeByType()
        {
            foreach (var token in new[] { "OfType<Grid>()", "OfType<WebView2>()", "Grid.GetColumn" })
                Assert.False(ProfilesCs.Contains(token),
                    $"MainWindow.Profiles.cs に '{token}' が残っています。" +
                    "型や列番号で視覚ツリーを探すと、ペインの構造を変えた瞬間に無言で壊れます。");
        }

        [Fact(Skip = "#345 段階 2B で TimelinePane.xaml に移すまで成り立たない")]
        public void HeaderColumns_AreDeclaredOnlyInXaml()
        {
            Assert.False(TimelineCs.Contains("Grid.SetColumn"),
                "MainWindow.Timeline.cs に 'Grid.SetColumn' が残っています。" +
                "ヘッダーの列番号は TimelinePane.xaml に一度だけ書かれているべきです（#337 の再発防止）。");
        }
    }
}
