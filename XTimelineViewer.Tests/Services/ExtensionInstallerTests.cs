using System;
using System.IO;
using System.Linq;
using System.Text;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// GitHub のリリースから拡張機能を入れる（#399）。
    ///
    /// <b>任意の URL から持ってきたコードを X のページ上で動かす</b>ことになるので、
    /// 「おかしいものを掴まない」ことと「何を許すのかを見せられる」ことを固定する。
    /// </summary>
    [Collection("AppLog")]
    public class ExtensionInstallerTests : IDisposable
    {
        private readonly string _dir;

        public ExtensionInstallerTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "xtv-inst-" + Guid.NewGuid().ToString("N"));
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

        // ── URL の読み取り ───────────────────────────────────────────────

        [Theory]
        [InlineData("https://github.com/gorhill/uBlock",                          "gorhill", "uBlock")]
        [InlineData("https://github.com/gorhill/uBlock/releases",                 "gorhill", "uBlock")]
        [InlineData("https://github.com/gorhill/uBlock/releases/tag/1.2.3",       "gorhill", "uBlock")]
        [InlineData("github.com/gorhill/uBlock",                                  "gorhill", "uBlock")]
        [InlineData("https://github.com/gorhill/uBlock.git",                      "gorhill", "uBlock")]
        [InlineData("https://www.github.com/some-owner/some.repo_name",           "some-owner", "some.repo_name")]
        public void ParseRepoUrl_AcceptsTheUsualForms(string url, string owner, string repo)
        {
            var r = ExtensionInstaller.ParseRepoUrl(url);
            Assert.NotNull(r);
            Assert.Equal(owner, r!.Value.Owner);
            Assert.Equal(repo,  r.Value.Repo);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("https://example.com/gorhill/uBlock")]
        [InlineData("https://gitlab.com/gorhill/uBlock")]
        [InlineData("https://github.com/gorhill")]
        [InlineData("not a url")]
        public void ParseRepoUrl_RejectsAnythingElse(string url)
            => Assert.Null(ExtensionInstaller.ParseRepoUrl(url));

        [Fact]
        public void ApiUrl_PointsAtTheLatestRelease()
            => Assert.Equal("https://api.github.com/repos/gorhill/uBlock/releases/latest",
                            ExtensionInstaller.LatestReleaseApiFor("gorhill", "uBlock"));

        // ── 資産の選択 ───────────────────────────────────────────────────

        private const string ReleaseJson = """
        {
          "assets": [
            { "name": "uBlock0_1.2.3.chromium.zip", "browser_download_url": "https://e/a.zip" },
            { "name": "uBlock0_1.2.3.crx",          "browser_download_url": "https://e/a.crx" },
            { "name": "uBlock0_1.2.3.firefox.xpi",  "browser_download_url": "https://e/a.xpi" },
            { "name": "source.tar.gz",              "browser_download_url": "https://e/a.tgz" }
          ]
        }
        """;

        [Fact]
        public void SelectCandidates_TakesZipAndCrxOnly()
        {
            var got = ExtensionInstaller.SelectCandidates(ReleaseJson).Select(c => c.Name).ToList();
            Assert.Equal(["uBlock0_1.2.3.chromium.zip", "uBlock0_1.2.3.crx"], got);
        }

        [Fact]
        public void SelectCandidates_NoAssets_IsEmpty()
            => Assert.Empty(ExtensionInstaller.SelectCandidates("""{"tag_name":"v1"}"""));

        [Fact]
        public void SelectCandidates_NothingUsable_IsEmpty()
            => Assert.Empty(ExtensionInstaller.SelectCandidates("""
               {"assets":[{"name":"notes.txt","browser_download_url":"https://e/a.txt"}]}
               """));

        // ── CRX の取り扱い ───────────────────────────────────────────────

        private static byte[] Crx3(uint headerLen, int payload = 32)
        {
            var b = new byte[12 + headerLen + payload];
            Encoding.ASCII.GetBytes("Cr24").CopyTo(b, 0);
            BitConverter.GetBytes(3u).CopyTo(b, 4);
            BitConverter.GetBytes(headerLen).CopyTo(b, 8);
            return b;
        }

        [Fact]
        public void ZipOffset_PlainZip_IsZero()
        {
            var b = new byte[32];
            b[0] = 0x50; b[1] = 0x4B; b[2] = 0x03; b[3] = 0x04;   // "PK\x03\x04"
            Assert.Equal(0, ExtensionInstaller.ZipOffsetOf(b));
        }

        [Fact]
        public void ZipOffset_Crx3_SkipsTheHeader()
            => Assert.Equal(12 + 40, ExtensionInstaller.ZipOffsetOf(Crx3(40)));

        [Fact]
        public void ZipOffset_Crx2_SkipsKeyAndSignature()
        {
            var b = new byte[16 + 10 + 20 + 32];
            Encoding.ASCII.GetBytes("Cr24").CopyTo(b, 0);
            BitConverter.GetBytes(2u).CopyTo(b, 4);
            BitConverter.GetBytes(10u).CopyTo(b, 8);    // 公開鍵長
            BitConverter.GetBytes(20u).CopyTo(b, 12);   // 署名長

            Assert.Equal(16 + 10 + 20, ExtensionInstaller.ZipOffsetOf(b));
        }

        [Fact]
        public void ZipOffset_Garbage_IsRejected()
            => Assert.Equal(-1, ExtensionInstaller.ZipOffsetOf(Encoding.ASCII.GetBytes("this is not an extension at all")));

        [Fact]
        public void ZipOffset_TooShort_IsRejected()
            => Assert.Equal(-1, ExtensionInstaller.ZipOffsetOf([1, 2, 3]));

        [Fact]
        public void ZipOffset_HeaderLongerThanTheFile_IsRejected()
        {
            // 壊れた（あるいは細工された）CRX で、ファイルの外を指すヘッダー長
            var b = new byte[32];
            Encoding.ASCII.GetBytes("Cr24").CopyTo(b, 0);
            BitConverter.GetBytes(3u).CopyTo(b, 4);
            BitConverter.GetBytes(uint.MaxValue).CopyTo(b, 8);

            Assert.Equal(-1, ExtensionInstaller.ZipOffsetOf(b));
        }

        // ── 中身の検査 ───────────────────────────────────────────────────

        private string MakeExtracted(string name, bool nested, string manifest)
        {
            var root = Path.Combine(_dir, name);
            var target = nested ? Path.Combine(root, "inner") : root;
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "manifest.json"), manifest);
            return root;
        }

        [Fact]
        public void FindRoot_TopLevel()
        {
            var root = MakeExtracted("a", nested: false, "{}");
            Assert.Equal(root, ExtensionInstaller.FindExtensionRoot(root));
        }

        [Fact]
        public void FindRoot_OneLevelDown()
        {
            // ZIP によっては 1 階層挟んでいる
            var root = MakeExtracted("b", nested: true, "{}");
            Assert.Equal(Path.Combine(root, "inner"), ExtensionInstaller.FindExtensionRoot(root));
        }

        [Fact]
        public void FindRoot_NoManifest_IsNull()
        {
            // manifest.json の無いものを入れると、起動のたびにエラーが出る
            var root = Path.Combine(_dir, "c");
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "docs", "readme.md"), "x");

            Assert.Null(ExtensionInstaller.FindExtensionRoot(root));
        }

        // ── 確認画面に出す情報 ───────────────────────────────────────────

        [Fact]
        public void ReadManifest_CollectsWhatItCanDo()
        {
            // 何を許すことになるのかを見せてから入れる
            var root = MakeExtracted("d", nested: false, """
            {
              "name": "テスト拡張",
              "version": "2.1",
              "permissions": ["cookies", "storage"],
              "host_permissions": ["https://x.com/*"],
              "content_scripts": [ { "matches": ["https://x.com/home"] } ]
            }
            """);

            var info = ExtensionInstaller.ReadManifest(root);

            Assert.NotNull(info);
            Assert.Equal("テスト拡張", info!.Value.Name);
            Assert.Equal("2.1", info.Value.Version);
            Assert.Contains("cookies", info.Value.Permissions);
            Assert.Contains("https://x.com/*", info.Value.Permissions);
            Assert.Contains("https://x.com/home", info.Value.Permissions);
        }

        [Fact]
        public void ReadManifest_NoPermissions_IsEmptyNotNull()
        {
            var root = MakeExtracted("e", nested: false, """{"name":"n","version":"1"}""");
            var info = ExtensionInstaller.ReadManifest(root);

            Assert.NotNull(info);
            Assert.Empty(info!.Value.Permissions);
        }

        [Fact]
        public void ReadManifest_BrokenJson_IsNull()
        {
            var root = MakeExtracted("f", nested: false, "{ this is not json");
            Assert.Null(ExtensionInstaller.ReadManifest(root));
        }

        [Fact]
        public void ReadManifest_Missing_IsNull()
            => Assert.Null(ExtensionInstaller.ReadManifest(Path.Combine(_dir, "no-such-dir")));

        // ── 入れ先の名前 ─────────────────────────────────────────────────

        [Theory]
        [InlineData("uBlock0_1.2.3.chromium.zip", "uBlock0_1.2.3.chromium")]
        [InlineData("ext.crx",                    "ext")]
        public void FolderName_DropsTheExtension(string asset, string expected)
            => Assert.Equal(expected, ExtensionInstaller.FolderNameFor(asset));

        [Fact]
        public void FolderName_StripsPathSeparators()
        {
            // 資産名は相手が決める。パスを飛び出させない。
            var name = ExtensionInstaller.FolderNameFor("..\\..\\evil.zip");
            Assert.DoesNotContain('\\', name);
            Assert.DoesNotContain('/', name);
        }

        [Fact]
        public void FolderName_Emptyish_FallsBack()
            => Assert.False(string.IsNullOrWhiteSpace(ExtensionInstaller.FolderNameFor(".zip")));
    }
}
