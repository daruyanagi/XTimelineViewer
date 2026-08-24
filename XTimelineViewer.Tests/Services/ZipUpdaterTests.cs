using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// ZIP 版の自前更新（#328 段階2）。
    ///
    /// ここが誤ると<b>ユーザーのインストール先を壊す</b>。取り返しがつかないので、
    /// 「落として展開する」より先に「おかしいものを掴まない」ことを固定する。
    ///
    /// 実物のリリース ZIP（90 MB）は使わず、小さな ZIP を作って確かめる。
    /// </summary>
    // ZipUpdater は AppLog へ書く。AppLog は静的なので、並列に走ると
    // 他のテストが「ログが空であること」を確かめている最中に書き込んでしまう。
    [Collection("AppLog")]
    public class ZipUpdaterTests : IDisposable
    {
        private readonly string _dir;

        public ZipUpdaterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "xtv-upd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
            catch { /* 一時ディレクトリの後始末。消せなくてもテスト結果には関係ない */ }
            GC.SuppressFinalize(this);
        }

        // ── 資産の選択 ───────────────────────────────────────────────────

        private const string ReleaseJson = """
        {
          "tag_name": "v2.0.3",
          "assets": [
            { "name": "XTimelineViewer-v2.0.3-win-x64.zip",          "browser_download_url": "https://e/x64.zip" },
            { "name": "XTimelineViewer-v2.0.3-win-x64.zip.sha256",   "browser_download_url": "https://e/x64.sha256" },
            { "name": "XTimelineViewer-v2.0.3-win-arm64.zip",        "browser_download_url": "https://e/a64.zip" },
            { "name": "XTimelineViewer-v2.0.3-win-arm64.zip.sha256", "browser_download_url": "https://e/a64.sha256" }
          ]
        }
        """;

        [Theory]
        [InlineData(Architecture.X64,   "https://e/x64.zip", "https://e/x64.sha256")]
        [InlineData(Architecture.Arm64, "https://e/a64.zip", "https://e/a64.sha256")]
        public void SelectAsset_PicksTheMatchingArchitecture(Architecture arch, string zip, string sum)
        {
            // arm64 機に x64 を配ると #267 の BadImageFormatException が再来する
            var asset = ZipUpdater.SelectAsset(ReleaseJson, arch);

            Assert.NotNull(asset);
            Assert.Equal(zip, asset!.ZipUrl);
            Assert.Equal(sum, asset.ChecksumUrl);
        }

        [Fact]
        public void SelectAsset_WithoutChecksum_ReturnsNull()
        {
            // v2.0.2 以前のリリースには .sha256 が無い。
            // 検証できないものを展開して起動しては本末転倒なので、対象外にする。
            const string json = """
            {
              "assets": [
                { "name": "XTimelineViewer-v2.0.2-win-x64.zip", "browser_download_url": "https://e/x64.zip" }
              ]
            }
            """;
            Assert.Null(ZipUpdater.SelectAsset(json, Architecture.X64));
        }

        [Fact]
        public void SelectAsset_NoAssets_ReturnsNull()
            => Assert.Null(ZipUpdater.SelectAsset("""{"tag_name":"v9"}""", Architecture.X64));

        // ── チェックサムの読み取り ────────────────────────────────────────

        [Fact]
        public void ParseChecksum_ReadsSha256SumFormat()
        {
            const string h = "850fdfeff21a8378db4b49a1ed425cbf0ca3bf94c873e10fb17c94439c4b68a9";
            Assert.Equal(h, ZipUpdater.ParseChecksum($"{h}  XTimelineViewer-v2.0.2-win-x64.zip"));
        }

        [Fact]
        public void ParseChecksum_IsCaseInsensitiveAndTrims()
        {
            const string upper = "850FDFEFF21A8378DB4B49A1ED425CBF0CA3BF94C873E10FB17C94439C4B68A9";
            Assert.Equal(upper.ToLowerInvariant(), ZipUpdater.ParseChecksum($"  {upper}  file.zip \r\n"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   \r\n  ")]
        [InlineData("not-a-hash  file.zip")]
        [InlineData("850fdfef  file.zip")]                       // 短すぎる
        [InlineData("zzzzdfeff21a8378db4b49a1ed425cbf0ca3bf94c873e10fb17c94439c4b68a9  f.zip")]  // 16 進でない
        public void ParseChecksum_RejectsGarbage(string content)
            => Assert.Null(ZipUpdater.ParseChecksum(content));

        // ── 展開したものの確からしさ ──────────────────────────────────────

        [Fact]
        public void LooksLikeApp_RequiresBothExeAndDll()
        {
            var d = Path.Combine(_dir, "probe");
            Directory.CreateDirectory(d);
            Assert.False(ZipUpdater.LooksLikeApp(d));

            File.WriteAllText(Path.Combine(d, "XTimelineViewer.exe"), "x");
            Assert.False(ZipUpdater.LooksLikeApp(d));           // exe だけでは足りない

            File.WriteAllText(Path.Combine(d, "XTimelineViewer.dll"), "x");
            Assert.True(ZipUpdater.LooksLikeApp(d));
        }

        // ── 置き換え先の決め方 ───────────────────────────────────────────

        [Fact]
        public void StagingDir_IsASiblingOfTheInstallDir()
        {
            // インストール先の「中」に作ると、置き換えで自分ごと消すことになる。
            // 同じ親の下に置けば、置き換えを改名だけで済ませられる。
            var install = Path.Combine("C:", "apps", "XTimelineViewer");
            var staging = ZipUpdater.StagingDirFor(install);

            Assert.Equal(Path.GetDirectoryName(install), Path.GetDirectoryName(staging));
            Assert.False(staging.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void StagingDir_IgnoresTrailingSeparator()
        {
            var a = ZipUpdater.StagingDirFor(Path.Combine("C:", "apps", "xTV"));
            var b = ZipUpdater.StagingDirFor(Path.Combine("C:", "apps", "xTV") + Path.DirectorySeparatorChar);
            Assert.Equal(a, b);
        }

        [Fact]
        public void BackupDir_IsDistinctFromInstallAndStaging()
        {
            var install = Path.Combine("C:", "apps", "xTV");
            Assert.NotEqual(install,                              ZipUpdater.BackupDirFor(install));
            Assert.NotEqual(ZipUpdater.StagingDirFor(install),     ZipUpdater.BackupDirFor(install));
        }

        // ── 書き込み可否 ─────────────────────────────────────────────────

        [Fact]
        public void CanWriteTo_TempDir_IsTrue() => Assert.True(ZipUpdater.CanWriteTo(_dir));

        [Fact]
        public void CanWriteTo_MissingDir_IsFalse()
            => Assert.False(ZipUpdater.CanWriteTo(Path.Combine(_dir, "does-not-exist")));

        [Fact]
        public void CanWriteTo_LeavesNoProbeFileBehind()
        {
            ZipUpdater.CanWriteTo(_dir);
            Assert.Empty(Directory.GetFiles(_dir, ".xtv-write-probe-*"));
        }

        // ステージング（.update）とバックアップ（.old）は親に作られるので、
        // 「ファイルを置けるか」ではなく「フォルダーを作れるか」で見る（#412）。
        [Fact]
        public void CanCreateDirIn_TempDir_IsTrue() => Assert.True(ZipUpdater.CanCreateDirIn(_dir));

        [Fact]
        public void CanCreateDirIn_MissingDir_IsFalse_AndCreatesNothing()
        {
            // Directory.CreateDirectory は途中のフォルダーごと作ってしまう。
            // 探りのつもりが本当に作ってしまっては、判定も後始末も狂う。
            var missing = Path.Combine(_dir, "no", "such", "place");

            Assert.False(ZipUpdater.CanCreateDirIn(missing));
            Assert.False(Directory.Exists(Path.Combine(_dir, "no")));
        }

        [Fact]
        public void CanCreateDirIn_LeavesNoProbeDirBehind()
        {
            ZipUpdater.CanCreateDirIn(_dir);
            Assert.Empty(Directory.GetDirectories(_dir, ".xtv-dir-probe-*"));
        }

        [Fact]
        public void StagingAndBackup_BothLiveInTheParent()
        {
            // 自前更新の可否がこの前提に乗っている（#412）。置き場を
            // 動かすなら ZipUpdateRunner.CheckEligibility も見直すこと。
            var install = Path.Combine(_dir, "app");
            Assert.Equal(_dir, Path.GetDirectoryName(ZipUpdater.StagingDirFor(install)));
            Assert.Equal(_dir, Path.GetDirectoryName(ZipUpdater.BackupDirFor(install)));
        }

        // ── ハッシュと展開（実ファイル） ──────────────────────────────────

        [Fact]
        public async Task ComputeSha256_MatchesKnownValue()
        {
            var f = Path.Combine(_dir, "a.txt");
            await File.WriteAllTextAsync(f, "abc");
            // SHA256("abc")
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                         await ZipUpdater.ComputeSha256Async(f));
        }

        [Fact]
        public void Extract_ReplacesLeftoversFromAPreviousAttempt()
        {
            var zip  = MakeAppZip();
            var dest = Path.Combine(_dir, "staging");

            Directory.CreateDirectory(dest);
            File.WriteAllText(Path.Combine(dest, "leftover.txt"), "前回の失敗の残骸");

            ZipUpdater.Extract(zip, dest);

            Assert.False(File.Exists(Path.Combine(dest, "leftover.txt")));
            Assert.True(ZipUpdater.LooksLikeApp(dest));
        }

        // ── 通しの動き（HTTP は差し替える） ───────────────────────────────

        [Fact]
        public async Task StageAsync_ChecksumMismatch_DoesNotExtract()
        {
            // 壊れた ZIP を展開して起動してしまうのが最悪。手前で止まること。
            var zip     = MakeAppZip();
            var install = Path.Combine(_dir, "install");
            Directory.CreateDirectory(install);

            var updater = new ZipUpdater(new HttpClient(new FakeHandler(
                zipBytes: File.ReadAllBytes(zip),
                checksum: new string('0', 64) + "  x.zip")));

            var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
                updater.StageAsync(new ZipUpdater.Asset("x.zip", "https://e/x.zip", "https://e/x.sha256"), install));

            Assert.Contains("ハッシュが一致しません", ex.Message);
            Assert.False(Directory.Exists(ZipUpdater.StagingDirFor(install)));
        }

        [Fact]
        public async Task StageAsync_ValidZip_ExtractsAndKeepsInstallDirUntouched()
        {
            var zip     = MakeAppZip();
            var install = Path.Combine(_dir, "install");
            Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(install, "settings-like.txt"), "触るな");

            var bytes = File.ReadAllBytes(zip);
            var hash  = await ZipUpdater.ComputeSha256Async(zip);
            var updater = new ZipUpdater(new HttpClient(new FakeHandler(bytes, $"{hash}  x.zip")));

            var staging = await updater.StageAsync(
                new ZipUpdater.Asset("x.zip", "https://e/x.zip", "https://e/x.sha256"), install);

            Assert.True(ZipUpdater.LooksLikeApp(staging));
            // この段階ではまだ置き換えない。インストール先は無傷であること。
            Assert.True(File.Exists(Path.Combine(install, "settings-like.txt")));
            Assert.False(File.Exists(Path.Combine(install, "XTimelineViewer.exe")));
        }

        [Fact]
        public async Task StageAsync_ReportsProgress()
        {
            var zip   = MakeAppZip();
            var bytes = File.ReadAllBytes(zip);
            var hash  = await ZipUpdater.ComputeSha256Async(zip);
            var install = Path.Combine(_dir, "install");
            Directory.CreateDirectory(install);

            var seen = new System.Collections.Generic.List<double>();
            var updater = new ZipUpdater(new HttpClient(new FakeHandler(bytes, $"{hash}  x.zip")));

            await updater.StageAsync(
                new ZipUpdater.Asset("x.zip", "https://e/x.zip", "https://e/x.sha256"),
                install, new Progress<double>(seen.Add));

            // Progress<T> はスレッドプールへ回すので、届くまで少し待つ
            for (int i = 0; i < 50 && seen.Count == 0; i++) await Task.Delay(20);
            Assert.NotEmpty(seen);
            Assert.All(seen, v => Assert.InRange(v, 0.0, 1.0));
        }

        [Fact]
        public async Task StageAsync_RemovesTheDownloadedZip()
        {
            // 90 MB 近い。残すとディスクを食い潰す。
            var zip   = MakeAppZip();
            var bytes = File.ReadAllBytes(zip);
            var hash  = await ZipUpdater.ComputeSha256Async(zip);
            var install = Path.Combine(_dir, "install");
            Directory.CreateDirectory(install);

            var updater = new ZipUpdater(new HttpClient(new FakeHandler(bytes, $"{hash}  keep-me.zip")));
            await updater.StageAsync(
                new ZipUpdater.Asset("keep-me.zip", "https://e/x.zip", "https://e/x.sha256"), install);

            var work = Path.Combine(Path.GetTempPath(), "xtv-update");
            var leftovers = Directory.Exists(work)
                ? Directory.GetFiles(work, "keep-me.zip", SearchOption.AllDirectories)
                : [];
            Assert.Empty(leftovers);
        }

        // ── ヘルパー ─────────────────────────────────────────────────────

        /// <summary>本体らしい中身を持つ小さな ZIP を作る。</summary>
        private string MakeAppZip()
        {
            var src = Path.Combine(_dir, "src-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "XTimelineViewer.exe"), "fake exe");
            File.WriteAllText(Path.Combine(src, "XTimelineViewer.dll"), "fake dll");
            Directory.CreateDirectory(Path.Combine(src, "Assets"));
            File.WriteAllText(Path.Combine(src, "Assets", "icon.txt"), "fake asset");

            var zip = Path.Combine(_dir, "app-" + Guid.NewGuid().ToString("N") + ".zip");
            ZipFile.CreateFromDirectory(src, zip);
            return zip;
        }

        /// <summary>.sha256 と .zip を返し分けるだけの差し替え用ハンドラー。</summary>
        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly byte[] _zip;
            private readonly string _checksum;

            internal FakeHandler(byte[] zipBytes, string checksum)
            {
                _zip = zipBytes;
                _checksum = checksum;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var isChecksum = request.RequestUri!.ToString().EndsWith(".sha256", StringComparison.Ordinal);
                var content = isChecksum
                    ? (HttpContent)new StringContent(_checksum)
                    : new ByteArrayContent(_zip);

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }
        }
    }
}
