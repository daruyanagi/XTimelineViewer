using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// ZIP 版の自前更新（#328 段階2）。ダウンロード → 検証 → 展開まで。
    ///
    /// winget 版はここを通さない。winget 管理下のフォルダーを自前で書き換えると
    /// 「インストール済みバージョン」の管理情報とズレるため、winget に任せる。
    ///
    /// <b>.sha256 は改ざん対策ではない。</b>ZIP と同じリリースに置いてあるので、
    /// 配信元を取られたら両方書き換えられる。ここで防げるのは転送中の破損だけ。
    /// 改ざんまで見るならコード署名（#336）が要る。それが入るまでの割り切り（#412）。
    ///
    /// UI に依存させない。テストプロジェクト（net8.0）からリンクして検証するため。
    /// </summary>
    internal sealed class ZipUpdater
    {
        private readonly HttpClient _http;

        internal ZipUpdater(HttpClient http) => _http = http;

        /// <summary>更新に使うリリース資産の組。</summary>
        internal sealed record Asset(string ZipName, string ZipUrl, string ChecksumUrl);

        // ── 純粋な部分（テストしやすいように IO と分ける） ───────────────────

        /// <summary>
        /// 動作中のプロセスに合う ZIP の名前を決める。
        /// arm64 機で x64 の ZIP に更新してしまうと #267 の二の舞になる。
        /// </summary>
        internal static string ArchSuffix(Architecture arch) => arch switch
        {
            Architecture.Arm64 => "win-arm64",
            Architecture.X64   => "win-x64",
            _ => throw new PlatformNotSupportedException($"未対応のアーキテクチャです: {arch}"),
        };

        /// <summary>
        /// リリース JSON（GitHub API）から、自分に合う ZIP と .sha256 を選ぶ。
        /// 見つからなければ null（更新できないだけで、アプリは動き続ける）。
        /// </summary>
        internal static Asset? SelectAsset(string releaseJson, Architecture arch)
        {
            var suffix = ArchSuffix(arch);
            using var doc = JsonDocument.Parse(releaseJson);
            if (!doc.RootElement.TryGetProperty("assets", out var assets)) return null;

            string? zipName = null, zipUrl = null, sumUrl = null;
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url  = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (name is null || url is null) continue;
                if (!name.Contains(suffix, StringComparison.OrdinalIgnoreCase)) continue;

                if (name.EndsWith(".zip.sha256", StringComparison.OrdinalIgnoreCase)) sumUrl = url;
                else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { zipName = name; zipUrl = url; }
            }

            // .sha256 が無いリリース（v2.0.2 以前）は自前更新の対象にしない。
            // 検証できないものを展開して起動するわけにはいかない。
            if (zipName is null || zipUrl is null || sumUrl is null) return null;
            return new Asset(zipName, zipUrl, sumUrl);
        }

        /// <summary>
        /// `sha256sum` 形式（"&lt;hash&gt;  &lt;filename&gt;"）から期待ハッシュを取り出す。
        /// ファイル名だけの行や空行しか無ければ null。
        /// </summary>
        internal static string? ParseChecksum(string content)
        {
            foreach (var line in content.Split('\n'))
            {
                var t = line.Trim();
                if (t.Length == 0) continue;
                var hash = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                if (hash.Length == 64 && hash.All(Uri.IsHexDigit)) return hash.ToLowerInvariant();
            }
            return null;
        }

        /// <summary>展開したものが本体らしいかを見る。空の ZIP や中身違いを掴まない。</summary>
        internal static bool LooksLikeApp(string dir)
            => File.Exists(Path.Combine(dir, "XTimelineViewer.exe"))
            && File.Exists(Path.Combine(dir, "XTimelineViewer.dll"));

        // ── IO を伴う部分 ────────────────────────────────────────────────

        internal static async Task<string> ComputeSha256Async(string path, CancellationToken ct = default)
        {
            await using var fs = File.OpenRead(path);
            var hash = await SHA256.HashDataAsync(fs, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// ZIP を落として一時ファイルへ書く。進捗は 0.0〜1.0（長さ不明なら報告しない）。
        /// </summary>
        internal async Task<string> DownloadAsync(
            string url, string destPath, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "XTimelineViewer");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength;
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(destPath);

            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total is > 0) progress?.Report((double)read / total.Value);
            }
            return destPath;
        }

        internal async Task<string> DownloadTextAsync(string url, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "XTimelineViewer");
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }

        /// <summary>
        /// ZIP を展開先へ広げる。展開先が残っていれば作り直す
        /// （前回の失敗が混ざったまま起動するのを防ぐ）。
        /// </summary>
        internal static void Extract(string zipPath, string destDir)
        {
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
            Directory.CreateDirectory(destDir);
            ZipFile.ExtractToDirectory(zipPath, destDir, overwriteFiles: true);
        }

        /// <summary>更新の作業ディレクトリ。インストール先の「隣」に置く。</summary>
        /// <remarks>
        /// インストール先の中に作ると、置き換えのときに自分ごと消すことになる。
        /// 同じドライブに置くことで、置き換えを改名だけで済ませられる（コピーが要らない）。
        /// </remarks>
        internal static string StagingDirFor(string installDir)
        {
            var parent = Path.GetDirectoryName(installDir.TrimEnd(Path.DirectorySeparatorChar))
                         ?? throw new InvalidOperationException($"親フォルダーを決められません: {installDir}");
            var name = Path.GetFileName(installDir.TrimEnd(Path.DirectorySeparatorChar));
            return Path.Combine(parent, name + ".update");
        }

        /// <summary>置き換え前に旧版を退避する先。</summary>
        internal static string BackupDirFor(string installDir)
            => installDir.TrimEnd(Path.DirectorySeparatorChar) + ".old";

        /// <summary>
        /// フォルダーを作れるか（#412）。
        ///
        /// ステージング（<c>.update</c>）とバックアップ（<c>.old</c>）は
        /// インストール先ではなく<b>その親</b>に作られる。親を見ずに始めると、
        /// 90 MB 落とし終えてから転ぶ。
        ///
        /// ファイルではなくフォルダーで試すのは、<c>C:\</c> 直下のような
        /// 「フォルダーは作れるがファイルは置けない」場所を取りこぼさないため。
        /// ZIP をドライブ直下に展開している人は珍しくない。
        /// </summary>
        internal static bool CanCreateDirIn(string dir)
        {
            // Directory.CreateDirectory は途中のフォルダーごと作ってしまう。
            // 無い場所に対して true を返したうえ、消し残しも出る。
            if (!Directory.Exists(dir)) return false;

            try
            {
                var probe = Path.Combine(dir, $".xtv-dir-probe-{Guid.NewGuid():N}");
                Directory.CreateDirectory(probe);
                Directory.Delete(probe);
                return true;
            }
            catch
            {
                // 権限が無い・読み取り専用など。理由は問わず「自前更新はしない」でよい。
                return false;
            }
        }

        /// <summary>
        /// 書き込めるか（Program Files 配下だと昇格が要る）。
        /// 書けないなら自前更新は諦めてリリースページへ誘導する。
        /// </summary>
        internal static bool CanWriteTo(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, $".xtv-write-probe-{Guid.NewGuid():N}");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch
            {
                // 権限が無い・読み取り専用など。理由は問わず「自前更新はしない」でよい。
                return false;
            }
        }

        /// <summary>
        /// 落として検証して展開するところまで。戻り値は展開先。
        /// 検証に失敗したら例外を投げ、展開はしない。
        /// </summary>
        internal async Task<string> StageAsync(
            Asset asset, string installDir, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            // 呼び出しごとに別の作業場所を使う。固定のパスだと、
            // 二重に走ったときに互いの ZIP を踏む。
            var work = Path.Combine(Path.GetTempPath(), "xtv-update", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            var zipPath = Path.Combine(work, asset.ZipName);

            try
            {
                var expected = ParseChecksum(await DownloadTextAsync(asset.ChecksumUrl, ct))
                    ?? throw new InvalidDataException($"{asset.ZipName}.sha256 を読めませんでした。");

                await DownloadAsync(asset.ZipUrl, zipPath, progress, ct);

                var actual = await ComputeSha256Async(zipPath, ct);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"ダウンロードした ZIP のハッシュが一致しません。expected={expected} actual={actual}");

                var staging = StagingDirFor(installDir);
                Extract(zipPath, staging);

                if (!LooksLikeApp(staging))
                    throw new InvalidDataException($"展開先に本体が見当たりません: {staging}");

                AppLog.Debug($"ZipUpdater: 展開まで完了 {staging}");
                return staging;
            }
            finally
            {
                // ZIP は 90 MB 近い。成功しても失敗しても残さない。
                try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); }
                catch (Exception ex) { AppLog.Debug($"ZipUpdater: 一時 ZIP を消せませんでした {ex.Message}"); }
            }
        }
    }
}
