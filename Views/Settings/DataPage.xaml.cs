using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
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

    public sealed partial class DataPage : Page
    {
        /// <summary>x:Bind のバインディングソース。XAML から参照される。</summary>
        public ObservableCollection<SavedQueryItem> SavedQueries { get; } = [];

        private SettingsWindow? _parent;

        public DataPage()
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
            PageTitle.Text = R.Get("Nav_Data");

            // Export Folder
            ExportFolderCard.Header      = R.Get("Settings_ExportFolder");
            ExportFolderCard.Description = _parent?.SettingsFolder ?? "";
            OpenFolderBtn.Content        = R.Get("Button_OpenFolder");

            // Saved Search Queries
            SavedQueriesExpander.Header      = R.Get("Settings_SavedQueries");
            SavedQueriesExpander.Description = R.Get("Settings_SavedQueries_Description");
            RebuildSavedQueriesItems();

            // Related settings
            RelatedHeader.Text      = R.Get("Settings_RelatedSettings");
            ProfilesLinkCard.Header = R.Get("Nav_Profiles");
        }

        private void ProfilesLinkCard_Click(object sender, RoutedEventArgs e)
            => _parent?.SelectPage("Profiles");

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

        private async void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_parent is null) return;
            var folder = _parent.SettingsFolder;
            Directory.CreateDirectory(folder);
            await Windows.System.Launcher.LaunchFolderPathAsync(folder);
        }
    }
}
