using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// 自前更新をしてよい環境かの判定（#328）。
    ///
    /// <b>やってはいけない場所で走らせないこと</b>が要点。
    /// winget 管理下を自前で書き換えると「インストール済みバージョン」の
    /// 管理情報とズレるし、MSIX 版はパッケージの整合性が壊れる。
    /// </summary>
    [Collection("AppLog")]      // ZipUpdater 経由で AppLog に触れうる
    public class ZipUpdateRunnerTests : IDisposable
    {
        private readonly string _dir;

        public ZipUpdateRunnerTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "xtv-elig-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
            catch { /* 一時ディレクトリの後始末。消せなくてもテスト結果には関係ない */ }
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void Packaged_IsNeverEligible()
        {
            // MSIX 版を自前で書き換えるとパッケージの整合性が壊れる。
            // Store / Windows Update に任せる。
            Assert.Equal(ZipUpdateRunner.Eligibility.Packaged,
                ZipUpdateRunner.CheckEligibility(InstallChannel.Packaged, isPackaged: true, _dir));
        }

        [Fact]
        public void Packaged_WinsOverEverythingElse()
        {
            // 経路の判定がどうであれ、packaged なら自前更新はしない
            Assert.Equal(ZipUpdateRunner.Eligibility.Packaged,
                ZipUpdateRunner.CheckEligibility(InstallChannel.Zip, isPackaged: true, _dir));
        }

        [Fact]
        public void Winget_IsNotEligible()
        {
            // winget 管理下のフォルダーを自前で書き換えると、
            // winget が持つ「インストール済みバージョン」とズレる。
            Assert.Equal(ZipUpdateRunner.Eligibility.ManagedByWinget,
                ZipUpdateRunner.CheckEligibility(InstallChannel.Winget, isPackaged: false, _dir));
        }

        [Fact]
        public void Zip_WritableDir_IsEligible()
            => Assert.Equal(ZipUpdateRunner.Eligibility.Ok,
                ZipUpdateRunner.CheckEligibility(InstallChannel.Zip, isPackaged: false, _dir));

        [Fact]
        public async Task RunAsync_ReleaseWithoutChecksum_IsNotSupported()
        {
            // v2.0.2 以前のリリースには .sha256 が無い。
            // 検証できないものを展開して起動するわけにはいかないので、
            // ダウンロードへ進む前に止まること。
            const string json = """
            {
              "assets": [
                { "name": "XTimelineViewer-v2.0.2-win-x64.zip",   "browser_download_url": "https://e/x64.zip" },
                { "name": "XTimelineViewer-v2.0.2-win-arm64.zip", "browser_download_url": "https://e/a64.zip" }
              ]
            }
            """;

            using var http = new HttpClient(new JsonOnlyHandler(json));
            var result = await ZipUpdateRunner.RunAsync(http, _dir);

            Assert.Equal(ZipUpdateRunner.RunResult.NotSupported, result);
            // 展開先を作っていないこと
            Assert.False(Directory.Exists(ZipUpdater.StagingDirFor(_dir)));
        }

        [Fact]
        public async Task RunAsync_NetworkFailure_ReportsFailedWithoutTouchingAnything()
        {
            using var http = new HttpClient(new ThrowingHandler());
            var result = await ZipUpdateRunner.RunAsync(http, _dir);

            Assert.Equal(ZipUpdateRunner.RunResult.Failed, result);
            Assert.False(Directory.Exists(ZipUpdater.StagingDirFor(_dir)));
        }

        /// <summary>リリース JSON だけを返す。</summary>
        private sealed class JsonOnlyHandler(string json) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(json),
                });
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new HttpRequestException("回線不通");
        }

        [Fact]
        public void Zip_UnwritableDir_IsNotEligible()
        {
            // Program Files 配下など。昇格が要るので自前更新は諦め、
            // リリースページへ誘導する。
            Assert.Equal(ZipUpdateRunner.Eligibility.NotWritable,
                ZipUpdateRunner.CheckEligibility(
                    InstallChannel.Zip, isPackaged: false, Path.Combine(_dir, "no-such-dir")));
        }
    }
}
