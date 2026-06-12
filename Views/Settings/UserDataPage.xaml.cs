using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.ComponentModel;
using System.IO;
using XTimelineViewer.ViewModels;

namespace XTimelineViewer.Views.Settings
{
    public sealed partial class UserDataPage : Page
    {
        private SettingsWindow? _parent;

        /// <summary>x:Bind のバインディングソース。XAML から参照される。</summary>
        public SettingsViewModel? VM { get; private set; }

        public UserDataPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _parent = e.Parameter as SettingsWindow;
            VM      = _parent?.ViewModel;
            if (VM is not null)
                VM.PropertyChanged += OnViewModelPropertyChanged;
            PopulateUI();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (VM is not null)
                VM.PropertyChanged -= OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(SettingsViewModel.LanguageIndex):
                    PopulateUI();
                    break;
                case nameof(SettingsViewModel.HasSavedQueries):
                    // 全件削除されたら Expander を畳む（IsEnabled はバインディングで追随）
                    if (VM?.HasSavedQueries == false)
                        SavedQueriesExpander.IsExpanded = false;
                    break;
            }
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
            VM?.ReloadSavedQueries(R.Get("Profile_Delete"));
            if (VM?.HasSavedQueries == false)
                SavedQueriesExpander.IsExpanded = false;

            // Related settings
            RelatedHeader.Text      = R.Get("Settings_RelatedSettings");
            ProfilesLinkCard.Header = R.Get("Nav_Profiles");

            Bindings.Update();
        }

        private void SavedQueryDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
                VM?.RemoveSavedQuery(path);
        }

        private void ProfilesLinkCard_Click(object sender, RoutedEventArgs e)
            => _parent?.SelectPage("Profiles");

        private async void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_parent is null) return;
            var folder = _parent.SettingsFolder;
            Directory.CreateDirectory(folder);
            await Windows.System.Launcher.LaunchFolderPathAsync(folder);
        }
    }
}
