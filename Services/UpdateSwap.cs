using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// インストール先の差し替え（#328 段階2）。
    ///
    /// 実行中の exe / DLL は上書きできないので、置き換えを行うプロセスは
    /// <b>置き換え対象の外</b>にいる必要がある。専用の更新用 exe を足す案もあったが、
    /// 「無署名の小さな実行ファイルがネットから落として展開して自分を置き換える」は
    /// #383 で Defender に引っかかった振る舞いそのもの。バイナリもスクリプトも増やさず、
    /// <b>展開した新しいバージョン自身</b>にこの役をやらせる。
    ///
    /// 旧版 : 展開 → 新版を --finish-update 付きで起動 → 自分は終了
    /// 新版 : 旧版の終了を待つ → 改名で差し替え → 本来の場所から起動 → 自分は終了
    ///
    /// 差し替えを改名だけで済ませるため、展開先は必ずインストール先の隣に置く
    /// （同じボリューム内なら <see cref="Directory.Move"/> は中身をコピーしない）。
    /// </summary>
    internal static class UpdateSwap
    {
        internal const string FinishArg = "--finish-update";

        // 実地で試したところ、展開直後の 210 MB をウイルス対策が走査している間は
        // 改名が通らない。5 秒では足りず、後で手で試すと通った。
        // この待ちは UI の無い仕上げ役プロセスで起きるので、長めに取っても
        // 「アプリが固まった」とは見えない（窓が無い）。
        private const int DefaultAttempts = 30;
        private const int DefaultDelayMs  = 1000;

        /// <summary>差し替えの結果。</summary>
        internal enum SwapResult
        {
            Succeeded,
            /// <summary>差し替えに失敗し、元に戻した。旧版はそのまま使える。</summary>
            RolledBack,
            /// <summary>戻すことにも失敗した。手当てが要る。</summary>
            Broken,
        }

        /// <summary>
        /// 旧 → .old、展開先 → 旧の場所、の順に改名する。
        ///
        /// 途中で失敗したら元に戻す。<b>「更新に失敗して起動しなくなる」のが最悪</b>で、
        /// 「更新できなかった」はやり直せる。
        /// </summary>
        internal static SwapResult Swap(
            string installDir, string stagingDir, int attempts = DefaultAttempts, int delayMs = DefaultDelayMs)
        {
            var backup = ZipUpdater.BackupDirFor(installDir);
            var movedAway = false;

            try
            {
                // 前回の残骸。消せなくても致命的ではない（後述のとおり改名を試みる）。
                if (Directory.Exists(backup)) DeleteBestEffort(backup);

                // それでも残っているなら、別名へ寄せてから進む。
                if (Directory.Exists(backup))
                    Directory.Move(backup, backup + "." + Guid.NewGuid().ToString("N")[..8]);

                MoveWithRetry(installDir, backup, attempts, delayMs);
                movedAway = true;

                // ここは移動ではなくコピー。<b>この処理を行っているプロセス自身が
                // 展開先から動いている</b>ため、展開先は自分の exe と DLL に掴まれていて
                // 動かせない（実地で試して分かった。30 秒粘っても改名できなかった）。
                // 読み取りはできるので、コピーなら通る。
                CopyDirectory(stagingDir, installDir, attempts, delayMs);

                if (!ZipUpdater.LooksLikeApp(installDir))
                    throw new InvalidDataException($"差し替え後に本体が見当たりません: {installDir}");

                AppLog.Debug($"UpdateSwap: 差し替え完了 {installDir}");
                return SwapResult.Succeeded;
            }
            catch (Exception ex)
            {
                AppLog.Error("UpdateSwap", ex);

                if (!movedAway) return SwapResult.RolledBack;   // まだ何も動かしていない

                try
                {
                    // コピーの途中で転んでいると、中途半端な installDir が残っている。
                    // これを消さずに戻すと、新旧が混ざったものが起動してしまう。
                    if (Directory.Exists(installDir)) Directory.Delete(installDir, recursive: true);

                    MoveWithRetry(backup, installDir, attempts, delayMs);
                    AppLog.Debug("UpdateSwap: 旧版へ戻した");
                    return SwapResult.RolledBack;
                }
                catch (Exception restoreEx)
                {
                    // ここまで来ると自動では直せない。ログに残して人手に委ねる。
                    AppLog.Error("UpdateSwap(rollback)", restoreEx);
                    return SwapResult.Broken;
                }
            }
        }

        /// <summary>
        /// ディレクトリの中身を丸ごとコピーする。
        ///
        /// 210 MB ほどあるので速くはないが、<b>動いているプロセスの居場所を
        /// 移動できない</b>以上ほかに手が無い。数秒で終わる。
        /// </summary>
        internal static void CopyDirectory(
            string source, string dest, int attempts = DefaultAttempts, int delayMs = DefaultDelayMs)
        {
            Directory.CreateDirectory(dest);

            foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(source, dir)));

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dest, Path.GetRelativePath(source, file));
                CopyFileWithRetry(file, to, attempts, delayMs);
            }
        }

        /// <summary>
        /// 1 ファイルのコピー。改名と同じく、掴まれているだけなら離すのを待つ。
        /// 展開直後のファイルはウイルス対策の走査に掴まれていることがある。
        /// </summary>
        private static void CopyFileWithRetry(string from, string to, int attempts, int delayMs)
        {
            for (var i = 1; ; i++)
            {
                try
                {
                    File.Copy(from, to, overwrite: true);
                    return;
                }
                catch (Exception ex) when (i < attempts && IsTransient(ex))
                {
                    if (i == 1) AppLog.Debug($"UpdateSwap: {from} が掴まれている。離すのを待つ");
                    Thread.Sleep(delayMs);
                }
            }
        }

        /// <summary>
        /// ディレクトリの改名を、掴まれていても少し粘って行う。
        ///
        /// 実地で試したところ、展開直後のフォルダーは
        /// <b>ウイルス対策のスキャンや検索インデックスに掴まれていて Move が転ぶ</b>。
        /// いずれも数秒で離すので、一度の失敗で諦めると「たいてい失敗する更新」になる。
        /// </summary>
        internal static void MoveWithRetry(
            string source, string dest, int attempts = DefaultAttempts, int delayMs = DefaultDelayMs)
        {
            // 待っても直らない失敗で粘らない。元が無いのに 5 秒待たせるのは、
            // 更新が固まったように見えるだけで何の役にも立たない。
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException($"移動元がありません: {source}");

            for (var i = 1; ; i++)
            {
                try
                {
                    Directory.Move(source, dest);
                    if (i > 1) AppLog.Debug($"UpdateSwap: {i} 回目で改名できた {source}");
                    return;
                }
                catch (Exception ex) when (i < attempts && IsTransient(ex))
                {
                    if (i == 1) AppLog.Debug($"UpdateSwap: {source} が掴まれている。離すのを待つ: {ex.Message}");
                    Thread.Sleep(delayMs);
                }
            }
        }

        /// <summary>
        /// 待てば直る見込みのある失敗か。
        /// 掴まれている（IOException）／一時的に触れない（UnauthorizedAccessException）だけを対象にし、
        /// 「元が無い」「行き先が既にある」のような、待っても変わらないものは即座に投げさせる。
        /// </summary>
        private static bool IsTransient(Exception ex) => ex switch
        {
            DirectoryNotFoundException => false,
            FileNotFoundException      => false,
            UnauthorizedAccessException => true,
            IOException                 => true,
            _                           => false,
        };

        /// <summary>
        /// 前回の更新で残った .old を片付ける。起動のたびに軽く試すだけ。
        /// 消せなくても支障は無いので、失敗しても黙って諦める。
        /// </summary>
        internal static void CleanupBackup(string installDir)
        {
            // 差し替えは展開先からのコピーで行うので、展開先も残る。両方片付ける。
            foreach (var dir in new[] { ZipUpdater.BackupDirFor(installDir), ZipUpdater.StagingDirFor(installDir) })
            {
                if (!Directory.Exists(dir)) continue;
                if (DeleteBestEffort(dir)) AppLog.Debug($"UpdateSwap: 掃除した {dir}");
            }
        }

        private static bool DeleteBestEffort(string dir)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
                return true;
            }
            catch (Exception ex)
            {
                // 直前まで動いていたプロセスがまだファイルを掴んでいることがある。
                // 次の起動でまた試すので、ここで粘る必要はない。
                AppLog.Debug($"UpdateSwap: {dir} を消せませんでした（次回また試す）: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 差し替え役へ渡す引数。インストール先と、終了を待つべきプロセス ID。
        /// </summary>
        internal static string[] BuildFinishArgs(string installDir, int waitForPid)
            => [FinishArg, installDir, waitForPid.ToString()];

        /// <summary>
        /// <see cref="BuildFinishArgs"/> の逆。形が合わなければ null。
        /// </summary>
        internal static (string InstallDir, int WaitForPid)? ParseFinishArgs(string[] args)
        {
            if (args.Length < 3) return null;
            if (!string.Equals(args[0], FinishArg, StringComparison.Ordinal)) return null;
            if (!int.TryParse(args[2], out var pid)) return null;
            if (string.IsNullOrWhiteSpace(args[1])) return null;
            return (args[1], pid);
        }

        /// <summary>
        /// 指定のプロセスが終わるまで待つ。既にいなければすぐ返る。
        /// 待ちきれなかった場合は false（差し替えは行わない）。
        /// </summary>
        internal static async Task<bool> WaitForExitAsync(
            int pid, TimeSpan timeout, CancellationToken ct = default)
        {
            Process proc;
            try
            {
                proc = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                return true;    // 既に終了している
            }

            using (proc)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(timeout);
                    await proc.WaitForExitAsync(cts.Token);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    AppLog.Debug($"UpdateSwap: PID {pid} の終了を待ちきれませんでした");
                    return false;
                }
            }
        }
    }
}
