using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// GitHub からの取得・展開・設置（#399）。
    ///
    /// <b>インストール先へ置くのは中身を確かめてからの最後</b>という順序を固定する。
    /// 途中で転んだときに半端なフォルダーが読み込み対象へ残ると、
    /// 起動のたびにエラーが出るうえ、何が入っているのか分からなくなる。
    /// </summary>
    [Collection("AppLog")]
    public class ExtensionInstallRunnerTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _extensionsDir;
        private readonly string _workRoot;

        public ExtensionInstallRunnerTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "xtv-run-" + Guid.NewGuid().ToString("N"));
            _extensionsDir = Path.Combine(_dir, "extensions");
            // 共有の temp を使わない。テストの残骸が利用者の temp に残るし、
            // 実際のインストールと同じ場所を踏む。
            _workRoot = Path.Combine(_dir, "work");
            Directory.CreateDirectory(_extensionsDir);
            AppLog.Initialize(Path.Combine(_dir, "error.log"));
        }

        public void Dispose()
        {
            AppLog.Initialize(Path.Combine(Path.GetTempPath(), "xtv-test-log-sink.log"));
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
            catch { /* 一時ディレクトリの後始末。消せなくてもテスト結果には関係ない */ }
            GC.SuppressFinalize(this);
        }

        // ── ヘルパー ─────────────────────────────────────────────────────

        /// <summary>拡張機能らしい中身の ZIP をバイト列で作る。</summary>
        private byte[] MakeExtensionZip(bool nested = false, string manifest = """
            {"manifest_version":3,"name":"テスト","version":"1.0","permissions":["cookies"]}
            """)
        {
            var src = Path.Combine(_dir, "src-" + Guid.NewGuid().ToString("N"));
            var target = nested ? Path.Combine(src, "inner") : src;
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "manifest.json"), manifest);
            File.WriteAllText(Path.Combine(target, "content.js"), "console.log(1)");

            var zip = Path.Combine(_dir, "z-" + Guid.NewGuid().ToString("N") + ".zip");
            ZipFile.CreateFromDirectory(src, zip);
            var bytes = File.ReadAllBytes(zip);
            File.Delete(zip);
            Directory.Delete(src, recursive: true);
            return bytes;
        }

        /// <summary>ZIP の前に CRX3 のヘッダーを付ける。</summary>
        private static byte[] WrapAsCrx3(byte[] zip)
        {
            const int headerLen = 24;
            var b = new byte[12 + headerLen + zip.Length];
            Encoding.ASCII.GetBytes("Cr24").CopyTo(b, 0);
            BitConverter.GetBytes(3u).CopyTo(b, 4);
            BitConverter.GetBytes((uint)headerLen).CopyTo(b, 8);
            zip.CopyTo(b, 12 + headerLen);
            return b;
        }

        private ExtensionInstallRunner RunnerFor(string json, byte[]? payload = null)
            => new(new HttpClient(new FakeHandler(json, payload)));

        private const string ReleaseJson = """
        {"assets":[{"name":"ext-1.0.zip","browser_download_url":"https://e/ext-1.0.zip"}]}
        """;

        private static readonly ExtensionInstaller.Candidate Candidate =
            new("ext-1.0.zip", "https://e/ext-1.0.zip");

        // ── 候補の取得 ───────────────────────────────────────────────────

        [Fact]
        public async Task FindCandidates_BadUrl_IsRejectedBeforeAnyRequest()
        {
            var (status, list) = await RunnerFor(ReleaseJson).FindCandidatesAsync("https://example.com/a/b");
            Assert.Equal(ExtensionInstallRunner.Status.BadUrl, status);
            Assert.Empty(list);
        }

        [Fact]
        public async Task FindCandidates_NoUsableAsset_IsReported()
        {
            var runner = RunnerFor("""{"assets":[{"name":"notes.txt","browser_download_url":"https://e/a"}]}""");
            var (status, _) = await runner.FindCandidatesAsync("https://github.com/o/r");
            Assert.Equal(ExtensionInstallRunner.Status.NoAsset, status);
        }

        [Fact]
        public async Task FindCandidates_NetworkFailure_IsReported()
        {
            var runner = new ExtensionInstallRunner(new HttpClient(new ThrowingHandler()));
            var (status, _) = await runner.FindCandidatesAsync("https://github.com/o/r");
            Assert.Equal(ExtensionInstallRunner.Status.NoRelease, status);
        }

        // ── 取得と検査 ───────────────────────────────────────────────────

        [Fact]
        public async Task Prepare_ReadsTheManifest_AndDoesNotInstallYet()
        {
            var runner = RunnerFor(ReleaseJson, MakeExtensionZip());

            var prepared = await runner.PrepareAsync(Candidate, _extensionsDir, workRoot: _workRoot);

            Assert.Equal(ExtensionInstallRunner.Status.Ok, prepared.Status);
            Assert.Equal("テスト", prepared.Name);
            Assert.Equal("1.0", prepared.Version);
            Assert.Contains("cookies", prepared.Permissions);

            // ここではまだ置かない。確認を取ってから。
            Assert.Empty(Directory.GetDirectories(_extensionsDir));
        }

        [Fact]
        public async Task Prepare_AcceptsCrx()
        {
            var runner = RunnerFor(ReleaseJson, WrapAsCrx3(MakeExtensionZip()));

            var prepared = await runner.PrepareAsync(
                new ExtensionInstaller.Candidate("ext.crx", "https://e/ext.crx"), _extensionsDir,
                workRoot: _workRoot);

            Assert.Equal(ExtensionInstallRunner.Status.Ok, prepared.Status);
        }

        [Fact]
        public async Task Prepare_AcceptsANestedLayout()
        {
            var runner = RunnerFor(ReleaseJson, MakeExtensionZip(nested: true));
            var prepared = await runner.PrepareAsync(Candidate, _extensionsDir, workRoot: _workRoot);
            Assert.Equal(ExtensionInstallRunner.Status.Ok, prepared.Status);
        }

        [Fact]
        public async Task Prepare_NotAnExtension_IsRejectedAndLeavesNothing()
        {
            // manifest.json の無い ZIP を掴まされた場合
            var src = Path.Combine(_dir, "junk");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "readme.md"), "x");
            var zipPath = Path.Combine(_dir, "junk.zip");
            ZipFile.CreateFromDirectory(src, zipPath);

            var runner = RunnerFor(ReleaseJson, File.ReadAllBytes(zipPath));
            var prepared = await runner.PrepareAsync(Candidate, _extensionsDir, workRoot: _workRoot);

            Assert.Equal(ExtensionInstallRunner.Status.NotAnExtension, prepared.Status);
            Assert.Empty(Directory.GetDirectories(_extensionsDir));
        }

        [Fact]
        public async Task Prepare_Garbage_IsRejected()
        {
            var runner = RunnerFor(ReleaseJson, Encoding.ASCII.GetBytes("これは ZIP でも CRX でもない"));
            var prepared = await runner.PrepareAsync(Candidate, _extensionsDir, workRoot: _workRoot);

            Assert.Equal(ExtensionInstallRunner.Status.NotAnExtension, prepared.Status);
            Assert.Empty(Directory.GetDirectories(_extensionsDir));
        }

        [Fact]
        public async Task Prepare_AlreadyInstalled_StopsBeforeDownloading()
        {
            Directory.CreateDirectory(Path.Combine(_extensionsDir, "ext-1.0"));

            var runner = RunnerFor(ReleaseJson, MakeExtensionZip());
            var prepared = await runner.PrepareAsync(Candidate, _extensionsDir, workRoot: _workRoot);

            Assert.Equal(ExtensionInstallRunner.Status.AlreadyInstalled, prepared.Status);
        }

        [Fact]
        public async Task Prepare_ReportsProgress()
        {
            var runner = RunnerFor(ReleaseJson, MakeExtensionZip());
            var seen = new System.Collections.Generic.List<double>();

            await runner.PrepareAsync(Candidate, _extensionsDir, new Progress<double>(seen.Add), workRoot: _workRoot);

            for (int i = 0; i < 50 && seen.Count == 0; i++) await Task.Delay(20);
            Assert.NotEmpty(seen);
            Assert.All(seen, v => Assert.InRange(v, 0.0, 1.0));
        }

        // ── 設置 ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Commit_PutsItWhereItIsLoadedFrom()
        {
            var runner = RunnerFor(ReleaseJson, MakeExtensionZip());
            var prepared = await runner.PrepareAsync(Candidate, _extensionsDir, workRoot: _workRoot);

            Assert.True(ExtensionInstallRunner.Commit(prepared, _extensionsDir));
            Assert.True(File.Exists(Path.Combine(_extensionsDir, "ext-1.0", "manifest.json")));

            // 読み込み対象として認識されること
            Assert.Single(ExtensionStore.EnumerateExtensionDirs(_extensionsDir));
        }

        [Fact]
        public async Task Commit_CleansUpTheTemporaryCopy()
        {
            var runner = RunnerFor(ReleaseJson, MakeExtensionZip());
            var prepared = await runner.PrepareAsync(Candidate, _extensionsDir, workRoot: _workRoot);
            var staged = prepared.StagedRoot!;

            ExtensionInstallRunner.Commit(prepared, _extensionsDir);

            Assert.False(Directory.Exists(staged));
        }

        [Fact]
        public async Task Discard_LeavesNothingBehind()
        {
            // 確認画面で「やめる」を選んだ場合
            var runner = RunnerFor(ReleaseJson, MakeExtensionZip());
            var prepared = await runner.PrepareAsync(Candidate, _extensionsDir, workRoot: _workRoot);
            var staged = prepared.StagedRoot!;

            ExtensionInstallRunner.Discard(prepared);

            Assert.False(Directory.Exists(staged));
            Assert.Empty(Directory.GetDirectories(_extensionsDir));
        }

        [Fact]
        public void Commit_WithoutAPreparedPayload_IsRefused()
        {
            var bad = new ExtensionInstallRunner.Prepared(
                ExtensionInstallRunner.Status.Failed, null, null, "", "", [], "https://e/x");

            Assert.False(ExtensionInstallRunner.Commit(bad, _extensionsDir));
        }

        // ── 差し替え用ハンドラー ─────────────────────────────────────────

        private sealed class FakeHandler(string json, byte[]? payload) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var isApi = request.RequestUri!.Host.Contains("api.github.com", StringComparison.Ordinal);
                HttpContent content = isApi || payload is null
                    ? new StringContent(json)
                    : new ByteArrayContent(payload);

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new HttpRequestException("回線不通");
        }
    }
}
