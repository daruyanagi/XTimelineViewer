using System;
using System.Diagnostics;
using System.IO;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// winget 版の更新を winget へ委ねるときの起こし方（#412）。
    ///
    /// 以前は <c>cmd /c timeout /t 2 &amp;&amp; winget upgrade</c> で
    /// 「2 秒待てば終わっているだろう」としていた。xTV はプロファイルの数だけ
    /// WebView2 のプロセスを抱えるので、終了に 2 秒以上かかることがある。
    /// まだ掴んでいるファイルを winget が置き換えにいけば失敗する。
    /// <b>秒数ではなく、自分の PID が消えるのを待つ。</b>
    /// ZIP 版の自前更新（<see cref="UpdateSwap.WaitForExitAsync"/>）は
    /// 元から PID を待っており、winget 版だけが取り残されていた。
    ///
    /// 判断と組み立てはここ、起動もここ。UI からは <see cref="TryStart"/> だけ呼ぶ。
    /// </summary>
    internal static class WingetUpdate
    {
        internal const string PackageId = "daruyanagi.XTimelineViewer";

        /// <summary>
        /// 終了を待つ上限（秒）。過ぎたら諦めて winget に進む。
        /// 待てなかったときに何もしないより、winget に失敗を報告させたほうがよい。
        /// </summary>
        internal const int WaitTimeoutSeconds = 60;

        /// <summary>
        /// PowerShell に渡す 1 行。<paramref name="pid"/> の終了を待ってから winget を起こす。
        ///
        /// <c>-ErrorAction SilentlyContinue</c> は二つの「異常でない失敗」を飲む。
        /// 既に終了していて PID が見つからない場合と、上限まで待ちきれなかった場合。
        /// どちらも winget には進んでよい。
        /// </summary>
        internal static string BuildCommand(int pid, string packageId = PackageId)
            => $"Wait-Process -Id {pid} -Timeout {WaitTimeoutSeconds} -ErrorAction SilentlyContinue; "
             + $"winget upgrade --id {packageId} --exact";

        /// <summary>
        /// winget を起こすための起動情報。
        ///
        /// 作業フォルダーをインストール先から離すのが要点。プロセスの
        /// カレントディレクトリはそのフォルダーを掴むので、winget が
        /// 置き換えにいけない。スタートメニューからでも <c>xtv.exe</c> 経由でも
        /// カレントディレクトリはインストール先になる（#264）ので、
        /// 引き継がせない。
        /// </summary>
        internal static ProcessStartInfo BuildStartInfo(int pid)
            => new()
            {
                FileName         = "powershell.exe",
                Arguments        = $"-NoProfile -Command \"{BuildCommand(pid)}\"",
                UseShellExecute  = true,
                WorkingDirectory = Path.GetTempPath(),
            };

        /// <summary>
        /// winget を起こす。起こせたら true。
        ///
        /// <b>false のときに呼び出し元がアプリを終了してはいけない。</b>
        /// 終了してしまうと、利用者から見ると［終了して更新］を押したのに
        /// 何も起きずに消えただけになる。
        /// </summary>
        internal static bool TryStart(int pid)
        {
            try
            {
                var proc = Process.Start(BuildStartInfo(pid));
                if (proc is null)
                {
                    AppLog.Debug("WingetUpdate: プロセスを起こせなかった");
                    return false;
                }
                AppLog.Debug($"WingetUpdate: winget upgrade を予約した（pid={pid} の終了を待つ）");
                return true;
            }
            catch (Exception ex)
            {
                // powershell.exe が無い・起動を止められている環境。
                AppLog.Error("WingetUpdate.TryStart", ex);
                return false;
            }
        }
    }
}
