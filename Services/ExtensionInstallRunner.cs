using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// GitHub のリリースから拡張機能を取ってきて置くまでの段取り（#399）。
    ///
    /// 判断は <see cref="ExtensionInstaller"/>、ここは IO と順序。
    /// <b>インストール先へ置くのは、中身を確かめてからの最後</b>にする。
    /// 途中で転んだときに、半端なフォルダーが読み込み対象に残らないようにするため。
    /// </summary>
    internal sealed class ExtensionInstallRunner
    {
        private readonly HttpClient _http;

        internal ExtensionInstallRunner(HttpClient http) => _http = http;

        internal enum Status
        {
            Ok,
            /// <summary>GitHub の URL として読めなかった。</summary>
            BadUrl,
            /// <summary>リリースが無い、あるいは取得できなかった。</summary>
            NoRelease,
            /// <summary>zip / crx の資産が無い。</summary>
            NoAsset,
            /// <summary>拡張機能の体を成していない（manifest.json が無い等）。</summary>
            NotAnExtension,
            /// <summary>同じ名前で既に入っている。</summary>
            AlreadyInstalled,
            Failed,
            Canceled,
        }

        /// <summary>取ってきて中身を確かめた結果。まだインストール先には置いていない。</summary>
        internal sealed record Prepared(
            Status Status,
            string? StagedRoot,
            string? FolderName,
            string Name,
            string Version,
            IReadOnlyList<string> Permissions,
            string SourceUrl,
            string? WorkDir = null);

        /// <summary>候補を挙げる。複数あれば呼び出し側で選ばせる。</summary>
        internal async Task<(Status Status, IReadOnlyList<ExtensionInstaller.Candidate> Candidates)>
            FindCandidatesAsync(string repoUrl, CancellationToken ct = default)
        {
            var parsed = ExtensionInstaller.ParseRepoUrl(repoUrl);
            if (parsed is null) return (Status.BadUrl, []);

            try
            {
                var json = await GetStringAsync(
                    ExtensionInstaller.LatestReleaseApiFor(parsed.Value.Owner, parsed.Value.Repo), ct);

                var candidates = ExtensionInstaller.SelectCandidates(json);
                return candidates.Count == 0 ? (Status.NoAsset, []) : (Status.Ok, candidates);
            }
            catch (OperationCanceledException) { return (Status.Canceled, []); }
            catch (Exception ex)
            {
                AppLog.Error($"ExtensionInstall.FindCandidates({repoUrl})", ex);
                return (Status.NoRelease, []);
            }
        }

        /// <summary>
        /// 落として展開し、中身を読む。<b>インストール先にはまだ置かない。</b>
        /// 何を許すことになるのかを見せてから確定させるため。
        /// </summary>
        /// <param name="workRoot">
        /// 一時ファイルの置き場。テストから自分の場所を渡すためにある。
        /// 共有の temp を使うと、テストと実際のインストールが同じ場所を踏む。
        /// </param>
        internal async Task<Prepared> PrepareAsync(
            ExtensionInstaller.Candidate candidate,
            string extensionsDir,
            IProgress<double>? progress = null,
            CancellationToken ct = default,
            string? workRoot = null)
        {
            var folderName = ExtensionInstaller.FolderNameFor(candidate.Name);

            if (Directory.Exists(Path.Combine(extensionsDir, folderName)))
                return Fail(Status.AlreadyInstalled, candidate.DownloadUrl);

            var root = workRoot ?? Path.Combine(Path.GetTempPath(), "xtv-ext-install");
            var work = Path.Combine(root, Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(work);

                var bytes = await DownloadAsync(candidate.DownloadUrl, progress, ct);

                // .crx は署名ヘッダー付きの ZIP。ZIP が始まる位置まで読み飛ばす。
                var offset = ExtensionInstaller.ZipOffsetOf(bytes);
                if (offset < 0) return Fail(Status.NotAnExtension, candidate.DownloadUrl, work);

                var zipPath = Path.Combine(work, "payload.zip");
                await File.WriteAllBytesAsync(zipPath, bytes[offset..], ct);

                var extracted = Path.Combine(work, "extracted");
                ZipFile.ExtractToDirectory(zipPath, extracted);

                var extensionRoot = ExtensionInstaller.FindExtensionRoot(extracted);
                if (extensionRoot is null) return Fail(Status.NotAnExtension, candidate.DownloadUrl, work);

                var info = ExtensionInstaller.ReadManifest(extensionRoot);
                if (info is null) return Fail(Status.NotAnExtension, candidate.DownloadUrl, work);

                return new Prepared(Status.Ok, extensionRoot, folderName,
                                    info.Value.Name, info.Value.Version, info.Value.Permissions,
                                    candidate.DownloadUrl, work);
            }
            catch (OperationCanceledException)
            {
                return Fail(Status.Canceled, candidate.DownloadUrl, work);
            }
            catch (Exception ex)
            {
                AppLog.Error($"ExtensionInstall.Prepare({candidate.Name})", ex);
                return Fail(Status.Failed, candidate.DownloadUrl, work);
            }
        }

        /// <summary>
        /// 確認が取れたので、インストール先へ移す。<b>ここで初めて読み込み対象に入る。</b>
        /// </summary>
        internal static bool Commit(Prepared prepared, string extensionsDir)
        {
            if (prepared.StagedRoot is null || prepared.FolderName is null) return false;

            var dest = Path.Combine(extensionsDir, prepared.FolderName);

            try
            {
                Directory.CreateDirectory(extensionsDir);
                ExtensionStore.CopyDirectory(prepared.StagedRoot, dest);
                AppLog.Debug($"ExtensionInstall: {prepared.FolderName} を入れた（{prepared.SourceUrl}）");
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error($"ExtensionInstall.Commit({prepared.FolderName})", ex);
                // 半端に置かれたものを読み込み対象に残さない
                try { if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true); }
                catch (Exception cleanupEx)
                {
                    AppLog.Debug($"ExtensionInstall: 後始末に失敗 {cleanupEx.Message}");
                }
                return false;
            }
            finally
            {
                CleanupStaging(prepared.WorkDir);
            }
        }

        /// <summary>取りやめたときに一時ファイルを片付ける。</summary>
        internal static void Discard(Prepared prepared) => CleanupStaging(prepared.WorkDir);

        private static void CleanupStaging(string? workDir)
        {
            if (workDir is null) return;

            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); }
            catch (Exception ex) { AppLog.Debug($"ExtensionInstall: 一時ファイルを消せませんでした {ex.Message}"); }
        }

        private static Prepared Fail(Status status, string url, string? work = null)
        {
            if (work is not null)
            {
                try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); }
                catch (Exception ex) { AppLog.Debug($"ExtensionInstall: 一時ファイルを消せませんでした {ex.Message}"); }
            }
            return new Prepared(status, null, null, "", "", [], url);
        }

        private async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "XTimelineViewer");
            req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }

        private async Task<byte[]> DownloadAsync(string url, IProgress<double>? progress, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "XTimelineViewer");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            using var dst = new MemoryStream();

            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                dst.Write(buffer, 0, n);
                read += n;
                if (total is > 0) progress?.Report((double)read / total.Value);
            }

            return dst.ToArray();
        }
    }
}
