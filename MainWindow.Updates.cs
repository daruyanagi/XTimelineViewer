using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.UI;

namespace XTimelineViewer
{
    public sealed partial class MainWindow : Window
    {
        // ── Update check ─────────────────────────────────────────────────────

        private static bool IsUpdateAvailable(Version current, Version latest)
        {
            if (PackageContext.IsPackaged)
                return latest.Major > current.Major
                    || (latest.Major == current.Major && latest.Minor > current.Minor);
            return latest > current;
        }

        private static async Task<(Version version, string tag, string releaseUrl)> FetchLatestReleaseAsync()
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "XTimelineViewer");
            var json = await client.GetStringAsync(
                "https://api.github.com/repos/daruyanagi/XTimelineViewer/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString()!;
            var url = doc.RootElement.GetProperty("html_url").GetString()!;
            return (new Version(tag.TrimStart('v')), tag, url);
        }

        private async Task CheckForUpdatesInBackgroundAsync()
        {
            await Task.Delay(3000);
            if (!NetworkInterface.GetIsNetworkAvailable()) return;

            if (_appSettings.LastUpdateCheck is { } raw
                && DateTime.TryParse(raw, null, DateTimeStyles.RoundtripKind, out var last)
                && (DateTime.UtcNow - last).TotalDays < 7)
                return;

            try
            {
                var (latest, tag, _) = await FetchLatestReleaseAsync();
                var current = Assembly.GetExecutingAssembly().GetName().Version!;
                _appSettings.LastUpdateCheck     = DateTime.UtcNow.ToString("O");
                _appSettings.CachedLatestVersion = IsUpdateAvailable(current, latest) ? tag : null;
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

        private async Task SelfUpdateViaWingetAsync(string releaseUrl)
        {
            var confirmDlg = new ContentDialog
            {
                Title             = R.Get("CheckUpdate_WingetTitle"),
                Content           = new TextBlock
                {
                    Text         = R.Get("CheckUpdate_WingetBody"),
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = R.Get("CheckUpdate_WingetConfirm"),
                CloseButtonText   = R.Get("Button_Cancel"),
                XamlRoot          = Content.XamlRoot,
                RequestedTheme    = ((FrameworkElement)Content).ActualTheme,
            };

            if (await ShowDialogAsync(confirmDlg) != ContentDialogResult.Primary) return;

            var winget = FindWinget();
            if (winget is null)
            {
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri(releaseUrl));
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName        = "cmd.exe",
                Arguments       = "/c timeout /t 2 /nobreak > nul && winget upgrade daruyanagi.XTimelineViewer",
                UseShellExecute = true,
            });
            Application.Current.Exit();
        }
    }
}
