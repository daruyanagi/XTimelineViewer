using System;
using System.IO;
using Xunit;

namespace XTimelineViewer.Tests
{
    /// <summary>
    /// ログの行き先をソースの文字列スキャンで固定する（#414）。
    ///
    /// <b>量の出る行を error.log へ戻さないこと。</b>
    /// 動画DL の GraphQL 傍受は応答のたびに 1 行出す。実測（v2.0.4）で
    /// error.log 16,022 行のうち 15,343 行がこれで、1 MB × 2 世代が半日で一周し、
    /// <b>UnhandledException の記録は 1 件も残っていなかった</b>。
    /// #340 が待っている「握りつぶした例外の蓄積」が永久に溜まらない状態だった。
    ///
    /// AppLog 側の分離は AppLogTests で見ている。ここで見るのは
    /// 「呼ぶ側が正しい方を呼んでいるか」。テストは net8.0 で WinUI 型に
    /// 触れないため、TimelinePaneStructureTests と同じくソースを読んで照合する。
    /// </summary>
    public class LogRoutingTests
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

        private static readonly string PostCs = File.ReadAllText(FindRepoFile("Views/MainWindow.Post.cs"));
        private static readonly string AppCs  = File.ReadAllText(FindRepoFile("App.xaml.cs"));

        /// <summary>波かっこを数えてメソッド本体を切り出す。</summary>
        private static string BodyOf(string source, string signature)
        {
            var at = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(at >= 0, $"{signature} が見つかりません");

            var open = source.IndexOf('{', at);
            Assert.True(open >= 0, $"{signature} の本体が見つかりません");

            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0)
                    return source[open..(i + 1)];
            }
            throw new InvalidOperationException($"{signature} の本体を閉じられません");
        }

        /// <summary>
        /// メンバーの中身を切り出す。<c>=&gt;</c> の式形式と波かっこの両方を扱う。
        ///
        /// 式形式に <see cref="BodyOf"/> をかけると、次に現れる <c>{</c> を拾って
        /// <b>後続メソッドの本体を返してしまう</b>（実際に踏んだ）。
        /// </summary>
        private static string MemberBodyOf(string source, string signature)
        {
            var at = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(at >= 0, $"{signature} が見つかりません");

            var after = at + signature.Length;
            var arrow = source.IndexOf("=>", after, StringComparison.Ordinal);
            var brace = source.IndexOf('{', after);

            if (arrow >= 0 && (brace < 0 || arrow < brace))
            {
                var end = source.IndexOf(';', arrow);
                Assert.True(end >= 0, $"{signature} の式を閉じられません");
                return source[arrow..(end + 1)];
            }

            return BodyOf(source, signature);
        }

        private static string CaptureVideoVariants
            => BodyOf(PostCs, "internal async Task CaptureVideoVariantsAsync(");

        [Fact]
        public void GraphQlInterception_WritesOnlyToTheDiagnosticsLog()
        {
            var body = CaptureVideoVariants;

            Assert.Contains("LogDiag(", body);
            Assert.DoesNotContain("LogDebug(", body);
        }

        [Fact]
        public void GraphQlInterception_DoesNotRecordItsFailuresAsErrors()
        {
            // GetContentAsync は「内容が残っていない」ときに投げる。検索の応答が
            // 差し替わって中断された場合などに起きる、想定内の失敗（#415）。
            // 実測では SearchTimeline の 45.8%（107 件中 49 件）がこれで、
            // error.log に載っていた例外 76 件は全部この 1 か所から出ていた。
            Assert.DoesNotContain("LogError(", CaptureVideoVariants);
        }

        // ── 環境情報（#427） ─────────────────────────────────────────────
        // ログの見出しとフィードバック本文に載る。間違っていると、
        // 報告を受け取った側が実在しない版を見ることになる。

        [Fact]
        public void WindowsAppSdkVersion_IsNotTakenFromWinUi()
        {
            // Microsoft.UI.Xaml.Application があるのは Microsoft.WinUI.dll で、
            // その FileVersion は WinUI 3 自身の版（3.0.0.2608）。
            // Windows App SDK の 1.x とは別系統なので、SDK の版として出すと
            // 実在しない版になる。実際にそうなっていた（#427）。
            var body = MemberBodyOf(AppCs, "private static string WinAppSdkVersion()");

            Assert.DoesNotContain("Microsoft.UI.Xaml.Application", body, StringComparison.Ordinal);
            Assert.DoesNotContain("FileVersion", body, StringComparison.Ordinal);
            Assert.Contains("WindowsAppRuntime.ReleaseInfo", body, StringComparison.Ordinal);
        }

        [Fact]
        public void WindowsAppSdkVersion_UsesTheReleaseNotTheRuntimeBinary()
        {
            // RuntimeInfo.AsString は 8000.946.1701.0 のようなランタイム
            // バイナリの版で、SDK の版ではない（実測）。
            var body = MemberBodyOf(AppCs, "private static string WinAppSdkVersion()");
            Assert.DoesNotContain("RuntimeInfo", body, StringComparison.Ordinal);
        }

        [Fact]
        public void DiagnosticsLog_IsNotUsedForOccasionalLines()
        {
            // 逆向きの歯止め。節目の 1 行まで diag.log へ流すと、
            // 今度は error.log を見ても何が起きたのか分からなくなる。
            foreach (var f in new[] { "Views/MainWindow.Updates.cs", "Services/UpdateSwap.cs",
                                      "Services/ZipUpdateRunner.cs", "Services/ExtensionStore.cs" })
            {
                Assert.DoesNotContain("AppLog.Diag(", File.ReadAllText(FindRepoFile(f)));
            }
        }
    }
}
