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
            // PrimaryLanguageOverride must be set before InitializeComponent() so that
            // XAML x:Uid bindings resolve without InvalidOperationException (#42).
            // For system language (lang == null) we still pin a concrete locale so WinRT
            // resource resolution always has an explicit language context.
            var lang = ReadLanguageSetting();
            var locale = lang ?? ResolveSystemLocale();
            try { Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = locale; }
            catch { /* unpackaged mode — R.Initialize() handles locale via resw */ }

            R.Initialize(lang);
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }

        private static string ResolveSystemLocale()
        {
            try
            {
                var twoLetter = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                return twoLetter == "ja" ? "ja-JP" : "en-US";
            }
            catch { return "en-US"; }
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
