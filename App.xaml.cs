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
        private Window? _window;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        public App()
        {
            SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var lang = ReadLanguageSetting();

            // Packaged (MSIX) mode: PrimaryLanguageOverride affects XAML x:Uid bindings
            if (lang != null)
            {
                try { Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang; }
                catch { /* unpackaged mode — R.Initialize() handles the override via resw */ }
            }

            R.Initialize(lang);
            _window = new MainWindow();
            _window.Activate();
        }

        private static string? ReadLanguageSetting()
        {
            try
            {
                string settingsPath;
                try
                {
                    settingsPath = Path.Combine(
                        Windows.Storage.ApplicationData.Current.LocalFolder.Path, "settings.json");
                }
                catch
                {
                    settingsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "XTimelineViewer", "settings.json");
                }

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