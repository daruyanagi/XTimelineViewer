using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;

namespace XTimelineViewer.Views.Settings
{
    public sealed partial class GeneralPage : Page
    {
        private static readonly string[] ThemeValues = ["Default", "Light", "Dark"];
        private static readonly string[] LangValues  = ["system", "ja-JP", "en-US"];

        private SettingsWindow? _parent;
        private bool _isInitializing = true;

        public GeneralPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _parent = e.Parameter as SettingsWindow;
            PopulateUI();
        }

        private void PopulateUI()
        {
            _isInitializing = true;

            PageTitle.Text = R.Get("Nav_General");

            // Theme
            ThemeCard.Header      = R.Get("Settings_Theme");
            ThemeCard.Description = R.Get("Settings_Theme_Description");
            ThemeCombo.ItemsSource = new List<string>
            {
                R.Get("Theme_System"),
                R.Get("Theme_Light"),
                R.Get("Theme_Dark"),
            };
            ThemeCombo.SelectedIndex = _parent?.Settings.Theme switch
            {
                "Light" => 1,
                "Dark"  => 2,
                _       => 0,
            };

            // Language
            LanguageCard.Header      = R.Get("Settings_Language");
            LanguageCard.Description = R.Get("Settings_Language_Description");
            LanguageCombo.ItemsSource = new List<string>
            {
                R.Get("Language_System"),
                R.Get("Language_JA"),
                R.Get("Language_EN"),
            };
            var langIdx = Array.IndexOf(LangValues, _parent?.Settings.Language ?? "system");
            LanguageCombo.SelectedIndex = langIdx < 0 ? 0 : langIdx;

            // Timeline Defaults
            DefaultTimelineExpander.Header      = R.Get("Settings_DefaultTimeline");
            DefaultTimelineExpander.Description = R.Get("Settings_DefaultTimeline_Description");
            if (_parent is not null)
            {
                var s = _parent.Settings;

                SidebarCard.Header       = R.Get("Settings_DefaultSidebar");
                SidebarToggle.OnContent  = R.Get("Toggle_Show");
                SidebarToggle.OffContent = R.Get("Toggle_Hide");
                SidebarToggle.IsOn       = !s.DefaultHideSidebar;

                ComposeCard.Header       = R.Get("Settings_DefaultCompose");
                ComposeToggle.OnContent  = R.Get("Toggle_Show");
                ComposeToggle.OffContent = R.Get("Toggle_Hide");
                ComposeToggle.IsOn       = !s.DefaultHideCompose;

                ListHeaderCard.Header       = R.Get("Settings_DefaultListHeader");
                ListHeaderToggle.OnContent  = R.Get("Toggle_Show");
                ListHeaderToggle.OffContent = R.Get("Toggle_Hide");
                ListHeaderToggle.IsOn       = !s.DefaultHideListHeader;
            }

            _isInitializing = false;
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _parent is null) return;

            var idx = Math.Clamp(ThemeCombo.SelectedIndex, 0, ThemeValues.Length - 1);
            _parent.Settings.Theme = ThemeValues[idx];

            // テーマは即時反映
            var theme = idx switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
            _parent.ApplyTheme(theme);
            _parent.NotifySettingsChanged();
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _parent is null) return;

            var idx = Math.Clamp(LanguageCombo.SelectedIndex, 0, LangValues.Length - 1);
            _parent.Settings.Language = LangValues[idx];
            _parent.NotifySettingsChanged();
        }

        private void SidebarToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _parent is null) return;
            _parent.Settings.DefaultHideSidebar = !SidebarToggle.IsOn;
            _parent.NotifySettingsChanged();
        }

        private void ComposeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _parent is null) return;
            _parent.Settings.DefaultHideCompose = !ComposeToggle.IsOn;
            _parent.NotifySettingsChanged();
        }

        private void ListHeaderToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _parent is null) return;
            _parent.Settings.DefaultHideListHeader = !ListHeaderToggle.IsOn;
            _parent.NotifySettingsChanged();
        }
    }
}
