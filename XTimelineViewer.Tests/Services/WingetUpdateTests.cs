using System.IO;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// winget 版の更新を winget へ委ねるときの起こし方（#412）。
    ///
    /// <b>秒数で待たない</b>ことと、<b>インストール先を掴んだまま渡さない</b>ことを固定する。
    /// どちらも「winget upgrade が黙って失敗する」形で出るので、
    /// 手で試しても気づきにくい。
    /// </summary>
    public class WingetUpdateTests
    {
        [Fact]
        public void Command_WaitsForThePid_NotForSeconds()
        {
            var cmd = WingetUpdate.BuildCommand(4321);

            // 自分の PID が消えるのを待つ。xTV はプロファイルの数だけ
            // WebView2 のプロセスを抱えるので、終了が 2 秒で終わる保証は無い。
            Assert.Contains("Wait-Process", cmd);
            Assert.Contains("-Id 4321", cmd);

            // 固定の待ち時間に戻っていないこと。
            // -Timeout は「待つ上限」なので別物（あってよい）。
            Assert.DoesNotContain("timeout /t", cmd, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Start-Sleep", cmd);
        }

        [Fact]
        public void Command_KeepsGoingWhenTheWaitFails()
        {
            // 既に終了していて PID が見つからない場合と、待ちきれなかった場合。
            // どちらも異常ではないので、止めずに winget へ進む。
            var cmd = WingetUpdate.BuildCommand(1);
            Assert.Contains("-ErrorAction SilentlyContinue", cmd);
            Assert.Contains($"-Timeout {WingetUpdate.WaitTimeoutSeconds}", cmd);
        }

        [Fact]
        public void Command_UpgradesThisPackageOnly()
        {
            var cmd = WingetUpdate.BuildCommand(1);
            Assert.Contains($"winget upgrade --id {WingetUpdate.PackageId} --exact", cmd);
        }

        [Fact]
        public void Command_NeedsNoQuoting()
        {
            // -Command "..." に丸ごと入れる。中に " が現れると壊れる。
            Assert.DoesNotContain("\"", WingetUpdate.BuildCommand(1));
        }

        [Fact]
        public void StartInfo_DoesNotHoldTheInstallDir()
        {
            // プロセスのカレントディレクトリはそのフォルダーを掴む。
            // インストール先を引き継ぐと、winget がそこを置き換えられない。
            // スタートメニューからでも xtv.exe 経由でも、カレントは
            // インストール先になっている（#264）。
            var psi = WingetUpdate.BuildStartInfo(1);

            Assert.False(string.IsNullOrEmpty(psi.WorkingDirectory));
            Assert.Equal(
                Path.GetFullPath(Path.GetTempPath()),
                Path.GetFullPath(psi.WorkingDirectory));
        }

        [Fact]
        public void StartInfo_RunsPowerShellWithoutTheUsersProfile()
        {
            var psi = WingetUpdate.BuildStartInfo(1);
            Assert.Equal("powershell.exe", psi.FileName);
            // プロファイルは重いうえ、何を出力するか分からない。
            Assert.Contains("-NoProfile", psi.Arguments);
            Assert.Contains(WingetUpdate.BuildCommand(1), psi.Arguments);
        }
    }
}
