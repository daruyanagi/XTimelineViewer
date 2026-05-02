using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Text.Json;

namespace XTimelineViewer
{
    public partial class App : Application
    {
        private Window? _window;

        public App()
        {
            ApplyLanguageOverride();
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }

        private static void ApplyLanguageOverride()
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

                if (!File.Exists(settingsPath)) return;

                using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
                if (doc.RootElement.TryGetProperty("Language", out var lang) &&
                    lang.GetString() is { } langStr && langStr != "system")
                {
                    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = langStr;
                }
            }
            catch { }
        }
    }
}
