using System;
using System.IO;
using Xunit;

namespace XTimelineViewer.Tests
{
    /// <summary>
    /// 起動経路の順序をソースの文字列走査で固定する（#435）。
    ///
    /// <c>App.OnLaunched</c> には<b>混ぜてはいけない 2 つの経路</b>がある。
    ///
    /// <list type="bullet">
    ///   <item>更新の仕上げ役（<c>--finish-update</c>）… 必ず別プロセスで走る必要がある</item>
    ///   <item>単一インスタンス化（#435）… 2 つ目以降を畳む</item>
    /// </list>
    ///
    /// 順序を逆にすると、<b>本体が動いているときの自前更新が静かに止まる</b>。
    /// 仕上げ役が「2 つ目の起動」と見なされて畳まれるため。更新は本体が
    /// 動いている状態から始まるので、常に踏む。
    ///
    /// テストは net8.0 で WinUI 型に触れないため、ソースを読んで照合する。
    /// </summary>
    public class StartupOrderTests
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

        private static readonly string AppCs = File.ReadAllText(FindRepoFile("App.xaml.cs"));

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

        [Fact]
        public void FinishUpdate_IsCheckedBeforeSingleInstance()
        {
            var body = BodyOf(AppCs, "protected override void OnLaunched(");

            var finish   = body.IndexOf("ParseFinishArgs", StringComparison.Ordinal);
            var redirect = body.IndexOf("RedirectToExistingInstance", StringComparison.Ordinal);

            Assert.True(finish   >= 0, "--finish-update の判定が見つかりません（#328）");
            Assert.True(redirect >= 0, "単一インスタンス化が見つかりません（#435）");
            Assert.True(finish < redirect,
                "単一インスタンス化が --finish-update の判定より前にあります。" +
                "仕上げ役が「2 つ目の起動」として畳まれ、自前更新が静かに止まります。");
        }

        [Fact]
        public void Redirect_EndsTheProcess_NotJustTheMethod()
        {
            // 渡したあと return するだけでは終わらない。ウィンドウを作って
            // いなくてもメッセージループは回り続け、見えないプロセスが
            // 起動のたびに積み上がる（実際にそうなった）。
            var body = BodyOf(AppCs, "private bool RedirectToExistingInstance()");

            Assert.Contains("Kill()", body, StringComparison.Ordinal);
        }

        [Fact]
        public void Redirect_WaitsForTheHandoverBeforeExiting()
        {
            // 渡し終える前に消えると、受け取る側が引数を読み切れずに RPC が落ちる。
            // また RedirectActivationToAsync を STA でそのまま待つと固まるので、
            // 別スレッドで走らせてこちらは待つ。
            var body = BodyOf(AppCs, "private bool RedirectToExistingInstance()");

            var run  = body.IndexOf("Task.Run", StringComparison.Ordinal);
            var wait = body.IndexOf(".Wait(", StringComparison.Ordinal);
            var kill = body.IndexOf("Kill()", StringComparison.Ordinal);

            Assert.True(run  >= 0, "リダイレクトを別スレッドで走らせていません（STA で待つと固まる）");
            Assert.True(wait >= 0, "渡し終えるのを待っていません");
            Assert.True(wait < kill, "待つ前に終了しています。受け取る側が引数を読み切れません。");
        }
    }
}
