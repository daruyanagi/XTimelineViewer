using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// ZIP 版の自前更新の段取り（#328）。
    ///
    /// 「落として検証して展開する」のは <see cref="ZipUpdater"/>、
    /// 「差し替える」のは <see cref="UpdateSwap"/>。ここはその二つを繋ぎ、
    /// <b>仕上げ役として新しいバージョンを起動する</b>ところまでを受け持つ。
    ///
    /// UI に依存させない。判断の理由をテストで固定したいため。
    /// </summary>
    internal static class ZipUpdateRunner
    {
        /// <summary>自前更新ができるか。できない理由も返す。</summary>
        internal enum Eligibility
        {
            Ok,
            /// <summary>MSIX 版。Store / Windows Update に任せる。</summary>
            Packaged,
            /// <summary>winget 版。管理情報とズレるので winget に任せる。</summary>
            ManagedByWinget,
            /// <summary>インストール先に書き込めない（Program Files 配下など）。</summary>
            NotWritable,
        }

        /// <summary>
        /// この環境で自前更新をしてよいか。
        /// 駄目なときはボタンを「リリースページを開く」のままにする。
        /// </summary>
        /// <param name="canCreateDirIn">
        /// 親にフォルダーを作れるかの判定。テストから「親には作れない」状況を
        /// 作るためにある（ACL をいじらずに済ませる）。既定は実物の探り。
        /// </param>
        internal static Eligibility CheckEligibility(
            InstallChannel channel, bool isPackaged, string installDir,
            Func<string, bool>? canCreateDirIn = null)
        {
            if (isPackaged) return Eligibility.Packaged;
            if (channel == InstallChannel.Winget) return Eligibility.ManagedByWinget;
            if (!ZipUpdater.CanWriteTo(installDir)) return Eligibility.NotWritable;

            // 展開先も退避先も親に作る。親に作れないなら始めるだけ無駄（#412）。
            // 親が無い（ドライブ直下に展開している）場合は置き場を作れない。
            var parent = Path.GetDirectoryName(installDir.TrimEnd(Path.DirectorySeparatorChar));
            if (parent is null) return Eligibility.NotWritable;
            if (!(canCreateDirIn ?? ZipUpdater.CanCreateDirIn)(parent)) return Eligibility.NotWritable;

            return Eligibility.Ok;
        }

        /// <summary>更新の実行結果。</summary>
        internal enum RunResult
        {
            /// <summary>展開まで済み、仕上げ役を起動した。呼び出し元はアプリを終了する。</summary>
            ReadyToRestart,
            /// <summary>このリリースは自前更新の対象外（.sha256 が無いなど）。</summary>
            NotSupported,
            /// <summary>途中で失敗した。何も置き換えていない。</summary>
            Failed,
            /// <summary>利用者が取り消した。</summary>
            Canceled,
        }

        /// <summary>
        /// 落として検証して展開し、仕上げ役を起動する。
        ///
        /// ここまでで<b>インストール先には一切触れていない</b>。
        /// 実際の差し替えは仕上げ役（新しいバージョン）が行う。
        /// </summary>
        internal static async Task<RunResult> RunAsync(
            HttpClient http,
            string installDir,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            try
            {
                var updater = new ZipUpdater(http);

                var json = await updater.DownloadTextAsync(AppUrls.LatestReleaseApi, ct);
                var asset = ZipUpdater.SelectAsset(json, RuntimeInformation.ProcessArchitecture);
                if (asset is null)
                {
                    // v2.0.2 以前のリリースには .sha256 が無い。検証できないものは扱わない。
                    AppLog.Debug("ZipUpdateRunner: 検証できる資産が見つからない（対象外）");
                    return RunResult.NotSupported;
                }

                var staging = await updater.StageAsync(asset, installDir, progress, ct);

                var newExe = Path.Combine(staging, "XTimelineViewer.exe");
                var args   = UpdateSwap.BuildFinishArgs(installDir, Environment.ProcessId);

                Process.Start(new ProcessStartInfo
                {
                    FileName         = newExe,
                    WorkingDirectory = staging,
                    UseShellExecute  = false,
                    ArgumentList     = { args[0], args[1], args[2] },
                });

                AppLog.Debug($"ZipUpdateRunner: 仕上げ役を起動した {newExe}");
                return RunResult.ReadyToRestart;
            }
            catch (OperationCanceledException)
            {
                AppLog.Debug("ZipUpdateRunner: 取り消された");
                return RunResult.Canceled;
            }
            catch (Exception ex)
            {
                AppLog.Error("ZipUpdateRunner", ex);
                return RunResult.Failed;
            }
        }
    }
}
