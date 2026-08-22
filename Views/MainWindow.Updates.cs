using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using XTimelineViewer.Services;

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

            // MSIX 版は Store / Windows Update の自動更新に任せる
            if (PackageContext.IsPackaged) return;

            // PowerToys にならい、24 時間ごとに確認する。失敗した場合は 2 時間後に再試行する。
            // xTV は起動しっぱなしで使うため、起動時 1 回だけでは数日間更新に気づけない (#328)。
            while (true)
            {
                var ok = await TryRefreshLatestVersionAsync();
                await Task.Delay(ok ? UpdateCheckInterval : UpdateRetryInterval);
            }
        }

        private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);
        private static readonly TimeSpan UpdateRetryInterval = TimeSpan.FromHours(2);

        /// <summary>
        /// 最新バージョンを取得してバッジ表示へ反映する。取得できたら true。
        /// </summary>
        private async Task<bool> TryRefreshLatestVersionAsync()
        {
            try
            {
                var latest = await FetchLatestVersionAsync();
                if (latest is null)
                {
                    // 失敗の内訳（回線不通・レート制限・パース失敗）はここまで伝わってこない。
                    // 「バッジが出ない」と言われたときに、そもそも確認できていないのかを見分ける（#382）。
                    AppLog.Debug("UpdateCheck: 最新バージョンを取得できなかった");
                    return false;
                }

                var current = Assembly.GetExecutingAssembly().GetName().Version!;
                _appSettings.CachedLatestVersion = latest > current
                    ? $"v{latest.ToString(3)}"
                    : null;
                _appSettings.LastUpdateCheck = DateTimeOffset.Now;
                AppLog.Debug($"UpdateCheck: current=v{current.ToString(3)} latest=v{latest.ToString(3)} "
                           + $"available={_appSettings.CachedLatestVersion is not null}");
                SaveSettings();
                UpdateMenuUpdateBadge();
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("UpdateCheck", ex);
                return false;
            }
        }

        /// <summary>
        /// 最新バージョンを取得する。winget 版は winget を、それ以外（ZIP 版）は
        /// GitHub Releases を参照する。ZIP 版は winget を持たないことがあり、従来は
        /// 更新に気づけなかった (#328)。
        /// </summary>
        internal static async Task<Version?> FetchLatestVersionAsync()
        {
            if (PackageContext.Channel == InstallChannel.Winget && FindWinget() is not null)
            {
                var viaWinget = await FetchWingetLatestVersionAsync();
                if (viaWinget is not null) return viaWinget;
            }
            return await FetchGitHubLatestVersionAsync();
        }

        /// <summary>
        /// GitHub Releases の最新タグ（v2.0.0 形式）からバージョンを取得する。
        /// 失敗時は null（ネットワーク不通、レート制限、パース失敗など）。
        /// </summary>
        private static async Task<Version?> FetchGitHubLatestVersionAsync()
        {
            try
            {
                using var req = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Get,
                    Services.AppUrls.LatestReleaseApi);
                // GitHub API は User-Agent 必須
                req.Headers.TryAddWithoutValidation("User-Agent", "XTimelineViewer");
                req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

                using var resp = await _updateHttp.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;

                using var doc = System.Text.Json.JsonDocument.Parse(
                    await resp.Content.ReadAsStreamAsync());
                if (!doc.RootElement.TryGetProperty("tag_name", out var tag)) return null;

                var text = tag.GetString()?.TrimStart('v', 'V');
                return Version.TryParse(text, out var v) ? v : null;
            }
            catch { return null; }
        }

        private static readonly System.Net.Http.HttpClient _updateHttp =
            new() { Timeout = TimeSpan.FromSeconds(15) };

        // 更新チェック用の 15 秒では 90 MB の ZIP を取り切れない。
        // 実際の打ち切りは CancellationToken（取り消しボタン）で行う。
        private static readonly System.Net.Http.HttpClient _downloadHttp =
            new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        private void UpdateMenuUpdateBadge()
        {
            var latest = _appSettings.CachedLatestVersion;
            var available = latest is not null;

            UpdateBadgeDot.Visibility = available ? Visibility.Visible : Visibility.Collapsed;

            // 青い点は「何かある」しか伝えられないので、メニューを開いた
            // ところで版数と行き先を示す（#382）。
            if (available)
            {
                UpdateAvailableMenuItem.Text = string.Format(R.Get("CheckUpdate_Available"), latest);
            }
            UpdateAvailableMenuItem.Visibility  = available ? Visibility.Visible : Visibility.Collapsed;
            UpdateAvailableSeparator.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// メニューからは設定のバージョン情報（更新欄）へ送る（#392）。
        ///
        /// 以前は外部ブラウザーでリリースページを開いていた。この項目を作った
        /// 時点（#382）では、ZIP 版にできることが「手で入れ直す」だけだったため。
        /// #328 でアプリ内から更新できるようになった後もそのままだったので、
        /// せっかくの「再起動して更新」に辿り着けず遠回りさせていた。
        ///
        /// 更新欄には経路に応じた操作（再起動して更新／終了して更新／
        /// リリースページを開く）が出るので、どの環境の人も適切な手段に届く。
        /// 更新の実行はアプリの終了を伴い戻せないので、メニューの
        /// 1 クリックでは起こさない、という方針は変えていない。
        /// </summary>
        private void UpdateAvailableMenuItem_Click(object sender, RoutedEventArgs e)
        {
            AppLog.Debug($"UpdateCheck: メニューから更新欄を開く latest={_appSettings.CachedLatestVersion}");
            OpenSettingsWindow("About");
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
