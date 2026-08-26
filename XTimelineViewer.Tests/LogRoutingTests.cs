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
