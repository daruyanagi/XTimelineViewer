using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using XTimelineViewer.Models;
using XTimelineViewer.Services;
using XTimelineViewer.Views;

namespace XTimelineViewer
{
    public partial class App : Application
    {
        // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
        // Must be called before InitializeComponent so WebView2 (Win32 HWND) and WinUI 3 (DIP)
        // coordinate systems are aligned, preventing scroll events hitting the wrong column on
        // non-100% DPI displays (125%, 150%, 200%).
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(nint value);

        private Window? _window;

        public App()
        {
            try
            {
                SetProcessDpiAwarenessContext(-4);
            }
            catch (Exception ex)
            {
                // ヘッドレス VM など DPI API が利用できない環境でもクラッシュしない
                Debug.WriteLine($"[App] SetProcessDpiAwarenessContext failed: {ex.Message}");
            }
            this.InitializeComponent();

            // UI スレッドの未処理例外でプロセスが即死するのを防ぐ。
            // winget バリデーション VM など特殊環境でのサイレントクラッシュを診断しやすくする。
            this.UnhandledException += (sender, e) =>
            {
                Debug.WriteLine($"[App] UnhandledException: {e.Exception}");
                LogUnhandledException(e.Exception);
                e.Handled = true;
            };
        }

        private static void LogUnhandledException(Exception ex)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "XTimelineViewer");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UnhandledException\n{ex}\n\n");
            }
            catch { /* ログ書き込み失敗は無視 */ }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // WebView2 が未初期化のうちに default プロファイルの初期化保留を処理する (#86)。
            // 実行中はブラウザプロセスがフォルダをロックするため、起動直後のここで削除する。
            ResetDefaultProfileIfPending();

            var lang = ReadLanguageSetting();

            if (lang != null && PackageContext.IsPackaged)
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang;

            R.Initialize(lang);
            _window = new MainWindow();
            _window.Activate();
        }

        private static string GetSettingsFilePath() =>
            PackageContext.IsPackaged
                ? Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "settings.json")
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "XTimelineViewer", "settings.json");

        // 初期化保留フラグが立っていれば、default の WebView2 データフォルダを削除する (#86)。
        private static void ResetDefaultProfileIfPending()
        {
            try
            {
                var settingsPath = GetSettingsFilePath();
                if (!File.Exists(settingsPath)) return;

                var settings = SettingsService.LoadSettings(settingsPath);
                if (!settings.ResetDefaultProfilePending) return;

                var dataPath = settings.DefaultProfileDataPath;
                if (!string.IsNullOrEmpty(dataPath) && Directory.Exists(dataPath))
                {
                    Directory.Delete(dataPath, recursive: true);
                    Debug.WriteLine($"[App] Default profile data deleted: {dataPath}");
                }

                settings.ResetDefaultProfilePending = false;
                SettingsService.SaveSettings(settingsPath, settings);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] ResetDefaultProfileIfPending FAILED: {ex.Message}");
            }
        }

        private static string? ReadLanguageSetting()
        {
            try
            {
                var settingsPath = GetSettingsFilePath();
                if (!File.Exists(settingsPath)) return null;

                using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
                if (doc.RootElement.TryGetProperty("Language", out var lang) &&
                    lang.GetString() is { } langStr && langStr != "system")
                {
                    Debug.WriteLine($"[App] Language setting: {langStr}");
                    return langStr;
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] ReadLanguageSetting FAILED: {ex.Message}");
                return null;
            }
        }
    }
}
