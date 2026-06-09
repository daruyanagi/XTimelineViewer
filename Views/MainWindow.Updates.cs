using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace XTimelineViewer.Views
{
    public sealed partial class MainWindow : Window
    {
        // ── Update check ─────────────────────────────────────────────────────

        /// <summary>
        /// winget show --versions の出力から最新バージョンを取得する。
        /// 失敗時は null を返す（winget 未インストール、ネットワーク不通、パース失敗など）。
        /// </summary>
        private static async Task<Version?> FetchWingetLatestVersionAsync()
        {
            var winget = FindWinget();
            if (winget is null) return null;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = winget,
                    Arguments              = "show daruyanagi.XTimelineViewer --versions --disable-interactivity",
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };
                using var proc = Process.Start(psi);
                if (proc is null) return null;

                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                if (proc.ExitCode != 0) return null;

                // "---" 区切り線より後の行からバージョンを抽出
                var lines = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var separatorIndex = Array.FindIndex(lines, l => l.StartsWith("---"));
                if (separatorIndex < 0) return null;

                // 区切り線の直後が最新バージョン（降順表示）
                for (int i = separatorIndex + 1; i < lines.Length; i++)
                {
                    if (Version.TryParse(lines[i], out var version))
                        return version;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// アプリ起動後にアイドルで更新チェックを行う。
        /// winget ベースで確認し、MSIX (Store) 版や winget のない環境ではスキップする。
        /// </summary>
        private async Task CheckForUpdatesInBackgroundAsync()
        {
            await Task.Delay(5000);

            // MSIX 版は Store の自動更新に任せる
            if (PackageContext.IsPackaged) return;

            // winget がなければチェック不要
            if (FindWinget() is null) return;

            try
            {
                var latest = await FetchWingetLatestVersionAsync();
                if (latest is null) return;

                var current = Assembly.GetExecutingAssembly().GetName().Version!;
                _appSettings.CachedLatestVersion = latest > current
                    ? $"v{latest.ToString(3)}"
                    : null;
                SaveSettings();
                UpdateMenuUpdateBadge();
            }
            catch { }
        }

        private void UpdateMenuUpdateBadge()
        {
            UpdateBadgeDot.Visibility = _appSettings.CachedLatestVersion is not null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static string? FindWinget()
        {
            var candidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "winget.exe");
            if (File.Exists(candidate)) return candidate;

            return Environment.GetEnvironmentVariable("PATH")
                ?.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(d => Path.Combine(d, "winget.exe"))
                .FirstOrDefault(File.Exists);
        }
    }
}
