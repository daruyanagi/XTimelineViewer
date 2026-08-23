using System;
using System.IO;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// 拡張機能の更新判定（#406）。
    ///
    /// <b>「分からないものを新しいと言わない」</b>のが要点。
    /// 誤って新しいと判断すると、要らない入れ替えを走らせて動いていたものを壊しうる。
    /// </summary>
    [Collection("AppLog")]
    public class ExtensionUpdaterTests : IDisposable
    {
        private readonly string _dir;

        public ExtensionUpdaterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "xtv-upd2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            AppLog.Initialize(Path.Combine(_dir, "error.log"));
        }

        public void Dispose()
        {
            AppLog.Initialize(Path.Combine(Path.GetTempPath(), "xtv-test-log-sink.log"));
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
            catch { /* 一時ディレクトリの後始末。消せなくてもテスト結果には関係ない */ }
            GC.SuppressFinalize(this);
        }

        // ── 入っている版を読む ───────────────────────────────────────────

        private string MakeExtension(string manifest)
        {
            var dir = Path.Combine(_dir, "ext-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "manifest.json"), manifest);
            return dir;
        }

        [Fact]
        public void InstalledVersion_ReadsTheManifest()
            => Assert.Equal("1.2.3",
                ExtensionUpdater.InstalledVersion(MakeExtension("""{"name":"n","version":"1.2.3"}""")));

        [Fact]
        public void InstalledVersion_NoManifest_IsNull()
            => Assert.Null(ExtensionUpdater.InstalledVersion(Path.Combine(_dir, "no-such-dir")));

        [Fact]
        public void InstalledVersion_BrokenJson_IsNull()
            => Assert.Null(ExtensionUpdater.InstalledVersion(MakeExtension("{ not json")));

        [Fact]
        public void InstalledVersion_NoVersionField_IsNull()
            => Assert.Null(ExtensionUpdater.InstalledVersion(MakeExtension("""{"name":"n"}""")));

        // ── タグを読む ───────────────────────────────────────────────────

        [Fact]
        public void Tag_IsRead()
            => Assert.Equal("v1.1.0", ExtensionUpdater.TagOf("""{"tag_name":"v1.1.0"}"""));

        [Fact]
        public void Tag_Missing_IsNull()
            => Assert.Null(ExtensionUpdater.TagOf("""{"name":"x"}"""));

        [Fact]
        public void Tag_BrokenJson_IsNull()
            => Assert.Null(ExtensionUpdater.TagOf("{ not json"));

        // ── 版を比べる ───────────────────────────────────────────────────

        [Theory]
        [InlineData("1.0.0", "1.0.1")]
        [InlineData("1.0",   "1.1")]
        [InlineData("1.9",   "1.10")]        // 文字列比較だと逆になる
        [InlineData("1.2.3", "2.0")]
        public void IsNewer_DetectsAnUpgrade(string installed, string latest)
            => Assert.True(ExtensionUpdater.IsNewer(installed, latest));

        [Theory]
        [InlineData("1.0.1", "1.0.0")]       // 古い
        [InlineData("1.0.0", "1.0.0")]       // 同じ
        [InlineData("2.0",   "1.9.9")]
        public void IsNewer_RejectsSameOrOlder(string installed, string latest)
            => Assert.False(ExtensionUpdater.IsNewer(installed, latest));

        [Theory]
        [InlineData("1.0.0", "v1.0.1")]      // タグは v が付くことがある
        [InlineData("1.0.0", "V1.0.1")]
        public void IsNewer_IgnoresTheTagPrefix(string installed, string latest)
            => Assert.True(ExtensionUpdater.IsNewer(installed, latest));

        [Theory]
        [InlineData("1.0.0", "1.0.1-beta")]  // 接尾辞は落として比べる
        [InlineData("1.0.0", "1.0.1+build")]
        public void IsNewer_IgnoresSuffixes(string installed, string latest)
            => Assert.True(ExtensionUpdater.IsNewer(installed, latest));

        [Theory]
        [InlineData(null,    "1.0.0")]
        [InlineData("1.0.0", null)]
        [InlineData("",      "1.0.0")]
        [InlineData("1.0.0", "")]
        [InlineData("latest", "1.0.0")]
        [InlineData("1.0.0", "latest")]
        [InlineData("1.0.0", "リリース名")]
        public void IsNewer_UnknownFormat_IsNotNewer(string? installed, string? latest)
        {
            // 分からないものを「新しい」と言わない。要らない入れ替えを走らせて
            // 動いていたものを壊すより、更新を見逃すほうがまし。
            Assert.False(ExtensionUpdater.IsNewer(installed, latest));
        }
    }
}
