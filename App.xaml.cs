using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace XTimelineViewer
{
    public partial class App : Application
    {
        private Window? _window;

        public App()
        {
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
