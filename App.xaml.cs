using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

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
            SetProcessDpiAwarenessContext(-4);
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var lang = ReadLanguageSetting();

            if (lang != null && PackageContext.IsPackaged)
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang;

            R.Initialize(lang);
            _window = new MainWindow();
            _window.Activate();
        }

        private static string? ReadLanguageSetting()
        {
            try
            {
                var settingsPath = PackageContext.IsPackaged
                    ? Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "settings.json")
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "XTimelineViewer", "settings.json");

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
