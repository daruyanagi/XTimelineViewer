using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace XTimelineViewer.Tests
{
    /// <summary>
    /// 対応 OS の下限が 2 か所に書かれているので、ずれないよう固定する（#436）。
    ///
    /// <c>XTimelineViewer.csproj</c> の <c>TargetPlatformMinVersion</c> と
    /// <c>Package.appxmanifest</c> の <c>MinVersion</c>。**片方だけ直す事故**は
    /// このリポジトリで前例がある（v1.3.1 でバージョンの更新漏れによりタグを
    /// 打ち直した。あちらは 3 桁 / 4 桁の食い違い）。
    ///
    /// 下限そのものは 19041（Windows 10 2004）。Windows App SDK 1.8 は 17763 まで
    /// 支えるが、#431 の Share 受信で使う外部ロケーション MSIX の登録が
    /// 19041 以上を要求するため、そちらに合わせてある。
    /// </summary>
    public class MinimumOsVersionTests
    {
        private static string FindRepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException($"リポジトリ内で {relative} が見つかりません");
        }

        private const string Expected = "10.0.19041.0";

        [Fact]
        public void Csproj_DeclaresTheExpectedMinimum()
        {
            var csproj = File.ReadAllText(FindRepoFile("XTimelineViewer.csproj"));
            var m = Regex.Match(csproj, @"<TargetPlatformMinVersion>([^<]+)</TargetPlatformMinVersion>");

            Assert.True(m.Success, "TargetPlatformMinVersion が見つかりません");
            Assert.Equal(Expected, m.Groups[1].Value.Trim());
        }

        [Fact]
        public void Appxmanifest_MatchesTheCsproj()
        {
            // 片方だけ直すと、MSIX の導入可否と実際に動く範囲が食い違う。
            var csproj   = File.ReadAllText(FindRepoFile("XTimelineViewer.csproj"));
            var manifest = File.ReadAllText(FindRepoFile("Package.appxmanifest"));

            var fromCsproj   = Regex.Match(csproj, @"<TargetPlatformMinVersion>([^<]+)</TargetPlatformMinVersion>").Groups[1].Value.Trim();
            var fromManifest = Regex.Match(manifest, @"MinVersion=""([^""]+)""").Groups[1].Value.Trim();

            Assert.Equal(fromCsproj, fromManifest);
        }

        [Fact]
        public void TheMinimumIsNotAboveWhatWeTarget()
        {
            // 下限が TargetFramework の版を超えていたら、そもそも噛み合っていない。
            var csproj = File.ReadAllText(FindRepoFile("XTimelineViewer.csproj"));

            var target = Regex.Match(csproj, @"<TargetFramework>net\d+\.\d+-windows([\d.]+)</TargetFramework>").Groups[1].Value;
            var min    = Regex.Match(csproj, @"<TargetPlatformMinVersion>([^<]+)</TargetPlatformMinVersion>").Groups[1].Value.Trim();

            Assert.True(Version.Parse(min) <= Version.Parse(target),
                $"下限 {min} が対象 {target} を超えています");
        }
    }
}
