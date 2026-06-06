using Microsoft.UI.Xaml;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace XTimelineViewer.Views
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
    }
}
