using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// インストール先の差し替え（#328 段階2）。
    ///
    /// <b>ここを誤るとユーザーの環境が起動しなくなる。</b>
    /// 「更新できなかった」はやり直せるが、「更新に失敗して壊れた」は戻せない。
    /// なので、成功する道より<b>失敗したときに元へ戻ること</b>を厚く確かめる。
    /// </summary>
    [Collection("AppLog")]      // AppLog を経由するので他の AppLog テストと直列に
    public class UpdateSwapTests : IDisposable
    {
        private readonly string _root;
        private readonly string _install;
        private readonly string _staging;

        public UpdateSwapTests()
        {
            _root    = Path.Combine(Path.GetTempPath(), "xtv-swap-" + Guid.NewGuid().ToString("N"));
            _install = Path.Combine(_root, "XTimelineViewer");
            _staging = ZipUpdater.StagingDirFor(_install);
            Directory.CreateDirectory(_root);

            // AppLog は静的。自分専用の出力先へ向けておかないと、
            // 他のテストが「ログが空であること」を確かめている最中に書き込んでしまう。
            AppLog.Initialize(Path.Combine(_root, "error.log"));
        }

        public void Dispose()
        {
            // 既定パス（実際の error.log）へ戻さないこと。ローテーションしてしまう。
            AppLog.Initialize(Path.Combine(Path.GetTempPath(), "xtv-test-log-sink.log"));
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
            catch { /* 一時ディレクトリの後始末。消せなくてもテスト結果には関係ない */ }
            GC.SuppressFinalize(this);
        }

        private void MakeInstall(string marker = "old") => MakeAppDir(_install, marker);
        private void MakeStaging(string marker = "new") => MakeAppDir(_staging, marker);

        /// <summary>
        /// 本体らしい中身を作る。差し替えは完了前に
        /// LooksLikeApp で確かめるので、exe だけでは足りない。
        /// </summary>
        private static void MakeAppDir(string dir, string marker)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "XTimelineViewer.exe"), marker);
            File.WriteAllText(Path.Combine(dir, "XTimelineViewer.dll"), marker);
            File.WriteAllText(Path.Combine(dir, "version.txt"), marker);
        }

        // ── 成功する道 ───────────────────────────────────────────────────

        [Fact]
        public void Swap_ReplacesInstallDirWithStaging()
        {
            MakeInstall();
            MakeStaging();

            Assert.Equal(UpdateSwap.SwapResult.Succeeded, UpdateSwap.Swap(_install, _staging, attempts: 4, delayMs: 25));

            Assert.Equal("new", File.ReadAllText(Path.Combine(_install, "version.txt")));
            // 差し替えはコピーで行うので展開先は残る。掃除は次回起動時（CleanupBackup）。
            Assert.True(Directory.Exists(_staging));
            Assert.True(Directory.Exists(ZipUpdater.BackupDirFor(_install)));   // 旧版は退避されている
            Assert.Equal("old", File.ReadAllText(Path.Combine(ZipUpdater.BackupDirFor(_install), "version.txt")));
        }

        [Fact]
        public void Swap_OverwritesAnOlderBackup()
        {
            // 2 回続けて更新したとき、前回の .old が残っていても進めること
            var backup = ZipUpdater.BackupDirFor(_install);
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, "version.txt"), "とても古い");

            MakeInstall();
            MakeStaging();

            Assert.Equal(UpdateSwap.SwapResult.Succeeded, UpdateSwap.Swap(_install, _staging, attempts: 4, delayMs: 25));
            Assert.Equal("old", File.ReadAllText(Path.Combine(backup, "version.txt")));
        }

        // ── 失敗したときに戻ること ────────────────────────────────────────

        [Fact]
        public void Swap_MissingStaging_LeavesInstallDirIntact()
        {
            // 展開先が消えていた場合。旧版が生き残ることがいちばん大事。
            MakeInstall();

            var result = UpdateSwap.Swap(_install, _staging, attempts: 4, delayMs: 25);

            Assert.Equal(UpdateSwap.SwapResult.RolledBack, result);
            Assert.True(Directory.Exists(_install));
            Assert.Equal("old", File.ReadAllText(Path.Combine(_install, "version.txt")));
        }

        [Fact]
        public void Swap_MissingInstallDir_DoesNotThrow()
        {
            MakeStaging();

            var result = UpdateSwap.Swap(_install, _staging, attempts: 4, delayMs: 25);

            Assert.NotEqual(UpdateSwap.SwapResult.Broken, result);
        }

        [Fact]
        public void Swap_StagingLocked_RestoresTheOldVersion()
        {
            // いちばん起こりそうな失敗。旧版を退避した後、新版の改名で転ぶ。
            // ここで戻せないと、インストール先が空のまま残って起動しなくなる。
            MakeInstall();
            MakeStaging();

            using (var hold = new FileStream(Path.Combine(_staging, "XTimelineViewer.exe"),
                                             FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var result = UpdateSwap.Swap(_install, _staging, attempts: 4, delayMs: 25);
                Assert.Equal(UpdateSwap.SwapResult.RolledBack, result);
            }

            Assert.True(Directory.Exists(_install));
            Assert.Equal("old", File.ReadAllText(Path.Combine(_install, "version.txt")));
        }

        // ── 掴まれているときの粘り ────────────────────────────────────────
        //
        // 実地で通しの差し替えを試したら、展開直後のフォルダーが
        // ウイルス対策のスキャンに掴まれていて Move が転んだ。数秒で離すので、
        // 一度の失敗で諦めると「たいてい失敗する更新」になる。

        [Fact]
        public async Task MoveWithRetry_SucceedsOnceTheLockIsReleased()
        {
            MakeInstall();
            var dest = Path.Combine(_root, "moved");

            var hold = new FileStream(Path.Combine(_install, "version.txt"),
                                      FileMode.Open, FileAccess.Read, FileShare.None);
            // 少し掴んでから離す
            var release = Task.Run(async () =>
            {
                await Task.Delay(250);
                hold.Dispose();
            });

            UpdateSwap.MoveWithRetry(_install, dest, attempts: 40, delayMs: 50);
            await release;

            Assert.True(Directory.Exists(dest));
            Assert.False(Directory.Exists(_install));
        }

        [Fact]
        public void MoveWithRetry_GivesUpEventually()
        {
            // 永久に粘ると、更新のたびにアプリが固まったように見える
            MakeInstall();
            using var hold = new FileStream(Path.Combine(_install, "version.txt"),
                                            FileMode.Open, FileAccess.Read, FileShare.None);

            Assert.ThrowsAny<Exception>(() =>
                UpdateSwap.MoveWithRetry(_install, Path.Combine(_root, "moved"), attempts: 3, delayMs: 10));
        }

        [Fact]
        public async Task Swap_TransientLock_StillSucceeds()
        {
            // 実地で起きたのはこれ。掴まれていても、離せば差し替えは通ること。
            MakeInstall();
            MakeStaging();

            var hold = new FileStream(Path.Combine(_staging, "version.txt"),
                                      FileMode.Open, FileAccess.Read, FileShare.None);
            var release = Task.Run(async () =>
            {
                await Task.Delay(250);
                hold.Dispose();
            });

            var result = UpdateSwap.Swap(_install, _staging, attempts: 40, delayMs: 25);
            await release;

            Assert.Equal(UpdateSwap.SwapResult.Succeeded, result);
            Assert.Equal("new", File.ReadAllText(Path.Combine(_install, "version.txt")));
        }

        // ── 後始末 ───────────────────────────────────────────────────────

        [Fact]
        public void CleanupBackup_RemovesBothBackupAndStaging()
        {
            // 差し替えがコピーになったので、展開先も残る。両方片付かないと
            // 210 MB のフォルダーが 2 つ居座り続ける。
            var backup = ZipUpdater.BackupDirFor(_install);
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, "a.txt"), "x");
            Directory.CreateDirectory(_staging);
            File.WriteAllText(Path.Combine(_staging, "b.txt"), "x");

            UpdateSwap.CleanupBackup(_install);

            Assert.False(Directory.Exists(backup));
            Assert.False(Directory.Exists(_staging));
        }

        [Fact]
        public void CleanupBackup_NothingToDo_DoesNotThrow()
            => UpdateSwap.CleanupBackup(_install);      // 例外が出ないこと

        [Fact]
        public void CleanupBackup_LockedBackup_DoesNotThrow()
        {
            // 直前まで動いていたプロセスがまだ掴んでいることがある。
            // 次の起動でまた試すので、ここで転んではいけない。
            var backup = ZipUpdater.BackupDirFor(_install);
            Directory.CreateDirectory(backup);
            var f = Path.Combine(backup, "held.txt");
            File.WriteAllText(f, "x");

            using var hold = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None);
            UpdateSwap.CleanupBackup(_install);         // 例外が出ないこと

            Assert.True(Directory.Exists(backup));      // 消えずに残る
        }

        // ── 引数の受け渡し ───────────────────────────────────────────────

        [Fact]
        public void FinishArgs_RoundTrip()
        {
            var args = UpdateSwap.BuildFinishArgs(@"C:\apps\xTV", 1234);
            var parsed = UpdateSwap.ParseFinishArgs(args);

            Assert.NotNull(parsed);
            Assert.Equal(@"C:\apps\xTV", parsed!.Value.InstallDir);
            Assert.Equal(1234, parsed.Value.WaitForPid);
        }

        // InlineData に配列を渡すと各要素が別の引数として展開されてしまうので、
        // params で受ける（xUnit の InlineData は params object[] のため）。
        [Theory]
        [InlineData]
        [InlineData("--finish-update")]
        [InlineData("--finish-update", @"C:\apps")]
        [InlineData("--finish-update", @"C:\apps", "not-a-pid")]
        [InlineData("--finish-update", "", "12")]
        [InlineData("--something-else", @"C:\apps", "12")]
        public void ParseFinishArgs_RejectsMalformed(params string[] args)
            => Assert.Null(UpdateSwap.ParseFinishArgs(args));

        [Fact]
        public void ParseFinishArgs_NormalStartup_IsNotMistakenForAnUpdate()
        {
            // 通常起動の引数で差し替えが走ったら大事故
            Assert.Null(UpdateSwap.ParseFinishArgs(["https://x.com/home"]));
        }

        // ── 旧プロセスの終了待ち ─────────────────────────────────────────

        [Fact]
        public async Task WaitForExit_AlreadyGone_ReturnsImmediately()
        {
            // 使われていない PID。既に終わっている扱いでよい。
            Assert.True(await UpdateSwap.WaitForExitAsync(int.MaxValue - 1, TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public async Task WaitForExit_StillRunning_TimesOut()
        {
            // 待ちきれないのに差し替えへ進むと、掴まれたままのファイルを動かすことになる
            using var proc = Process.Start(new ProcessStartInfo
            {
                // timeout /t は入力がリダイレクトされていると即エラー終了してしまい、
                // 「まだ動いている相手」にならない。ping なら黙って待つ。
                FileName        = "cmd.exe",
                Arguments       = "/c ping -n 60 127.0.0.1",
                CreateNoWindow  = true,
                UseShellExecute = false,
            })!;

            try
            {
                Assert.False(await UpdateSwap.WaitForExitAsync(proc.Id, TimeSpan.FromMilliseconds(300)));
            }
            finally
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* 後始末。既に落ちていれば何もしない */ }
            }
        }

        [Fact]
        public async Task WaitForExit_ExitsDuringWait_ReturnsTrue()
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName        = "cmd.exe",
                Arguments       = "/c exit",
                CreateNoWindow  = true,
                UseShellExecute = false,
            })!;

            Assert.True(await UpdateSwap.WaitForExitAsync(proc.Id, TimeSpan.FromSeconds(10)));
        }
    }
}
