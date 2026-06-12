using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace XTimelineViewer.Views.Settings
{
    /// <summary>保存済み検索クエリの表示用アイテム。x:Bind から参照される。</summary>
    public sealed class SavedQueryItem(string path, string decoded, string deleteLabel)
    {
        public string Path        { get; set; } = path;
        public string Decoded     { get; set; } = decoded;
        public string DeleteLabel { get; set; } = deleteLabel;
    }

    public sealed partial class GeneralPage : Page
    {
        /// <summary>x:Bind のバインディングソース。XAML から参照される。</summary>
        public ObservableCollection<SavedQueryItem> SavedQueries { get; } = [];

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

            // Export Folder
            ExportFolderCard.Header      = R.Get("Settings_ExportFolder");
            ExportFolderCard.Description = _parent?.SettingsFolder ?? "";
            OpenFolderBtn.Content        = R.Get("Button_OpenFolder");

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

            // Saved Search Queries
            SavedQueriesExpander.Header      = R.Get("Settings_SavedQueries");
            SavedQueriesExpander.Description = R.Get("Settings_SavedQueries_Description");
            RebuildSavedQueriesItems();

            _isInitializing = false;
        }

        private void RebuildSavedQueriesItems()
        {
            SavedQueries.Clear();
            if (_parent is null) return;

            var deleteLabel = R.Get("Profile_Delete");
            foreach (var path in _parent.Settings.SavedSearchQueries)
                SavedQueries.Add(new SavedQueryItem(path, Uri.UnescapeDataString(path), deleteLabel));

            UpdateSavedQueriesExpanderState();
        }

        private void UpdateSavedQueriesExpanderState()
        {
            if (SavedQueries.Count == 0)
            {
                SavedQueriesExpander.IsExpanded = false;
                SavedQueriesExpander.IsEnabled  = false;
            }
            else
            {
                SavedQueriesExpander.IsEnabled = true;
            }
        }

        private void SavedQueryDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_parent is null || sender is not Button btn || btn.Tag is not string path) return;
            _parent.Settings.SavedSearchQueries.Remove(path);
            _parent.NotifySettingsChanged();

            var item = SavedQueries.FirstOrDefault(q => q.Path == path);
            if (item is not null) SavedQueries.Remove(item);
            UpdateSavedQueriesExpanderState();
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

        private async void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_parent is null) return;
            var folder = _parent.SettingsFolder;
            Directory.CreateDirectory(folder);
            await Windows.System.Launcher.LaunchFolderPathAsync(folder);
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
