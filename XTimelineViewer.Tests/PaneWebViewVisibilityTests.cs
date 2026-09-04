using System;
using System.IO;
using System.Linq;
using Xunit;

namespace XTimelineViewer.Tests
{
    /// <summary>
    /// ペインの WebView2 を出し入れする判断を 1 か所に閉じ込める（#418）。
    ///
    /// WinUI 3 の WebView2 は msedgewebview2.exe 側にトップレベルの WS_POPUP を持ち、
    /// それをコントロールの画面絶対座標へ置く。ScrollViewer のクリップは見た目にしか
    /// 効かないので、ウィンドウ外へ出たペインの矩形がデスクトップのクリックを奪う。
    /// 消す手段は Visibility.Collapsed だけ（Opacity や IsHitTestVisible では消えない）。
    ///
    /// つまり「どのペインを出すか」は位置と、ダイアログを重ねているかの両方で決まる。
    /// 投稿ダイアログ（#244）と検索ダイアログ（#315）はかつて Panes を直接舐めて
    /// Visible へ戻しており、そのまま残すと画面外のペインまで復活してしまう。
    /// 判断を MainWindow.PaneVisibility.cs へ集約したので、戻らないよう固定する。
    ///
    /// ユニットテストからは WinUI 型に触れないため（テストは net8.0）、
    /// TimelinePaneStructureTests と同じくソースを読んで照合する。
    /// </summary>
    public class PaneWebViewVisibilityTests
    {
        private static DirectoryInfo FindRepoDir(string relative)
        {
            var rel = relative.Replace('/', Path.DirectorySeparatorChar);
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, rel);
                if (Directory.Exists(candidate)) return new DirectoryInfo(candidate);
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException($"リポジトリ内で {relative} が見つかりません");
        }

        /// <summary>判断を持ってよい唯一のファイル。</summary>
        private const string OwnerFile = "MainWindow.PaneVisibility.cs";

        [Fact]
        public void PaneWebViewVisibility_IsDecidedInOnePlace()
        {
            var views = FindRepoDir("Views");

            var offenders = views
                .EnumerateFiles("*.cs", SearchOption.AllDirectories)
                .Where(f => f.Name != OwnerFile)
                .Where(f => File.ReadAllText(f.FullName).Contains("WebView.Visibility ="))
                .Select(f => f.Name)
                .ToList();

            Assert.True(offenders.Count == 0,
                $"{string.Join(", ", offenders)} がペインの WebView2 の Visibility を直接書き換えています。" +
                $"位置による判定（#418）と食い違うので、{OwnerFile} の " +
                "SuppressPaneWebViews / ResumePaneWebViews を使ってください。");
        }

        [Fact]
        public void PaneVisibility_HasBothSuppressAndResume()
        {
            var owner = Path.Combine(FindRepoDir("Views").FullName, OwnerFile);
            var source = File.ReadAllText(owner);

            Assert.Contains("SuppressPaneWebViews", source);
            Assert.Contains("ResumePaneWebViews", source);
        }
    }
}
