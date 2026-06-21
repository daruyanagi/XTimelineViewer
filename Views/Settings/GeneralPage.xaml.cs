using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;
using System.ComponentModel;
using XTimelineViewer.Services;
using XTimelineViewer.ViewModels;

namespace XTimelineViewer.Views.Settings
{
    public sealed partial class GeneralPage : Page
    {
        private SettingsWindow? _parent;
        private List<EdgeProfile> _edgeProfiles = [];

        /// <summary>x:Bind のバインディングソース。XAML から参照される。</summary>
        public SettingsViewModel? VM { get; private set; }

        public GeneralPage()
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
            // VM はウィンドウと同寿命なので、ページ破棄時に購読を解除してリークを防ぐ
            if (VM is not null)
                VM.PropertyChanged -= OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                // 言語変更時はこのページ自身の表示も新しい言語で再構築する
                case nameof(SettingsViewModel.LanguageIndex):
                    PopulateUI();
                    break;
                case nameof(SettingsViewModel.IsEdgeSelected):
                    UpdateEdgeProfileComboEnabled();
                    break;
            }
        }

        private void UpdateEdgeProfileComboEnabled()
            => EdgeProfileCombo.IsEnabled = (VM?.IsEdgeSelected ?? false) && _edgeProfiles.Count > 0;

        private void PopulateUI()
        {
            PageTitle.Text = R.Get("Nav_General");

            // Timeline Defaults
            DefaultTimelineExpander.Header      = R.Get("Settings_DefaultTimeline");
            DefaultTimelineExpander.Description = R.Get("Settings_DefaultTimeline_Description");
            SidebarCard.Header       = R.Get("Settings_DefaultSidebar");
            SidebarToggle.OnContent  = R.Get("Toggle_Show");
            SidebarToggle.OffContent = R.Get("Toggle_Hide");
            ComposeCard.Header       = R.Get("Settings_DefaultCompose");
            ComposeToggle.OnContent  = R.Get("Toggle_Show");
            ComposeToggle.OffContent = R.Get("Toggle_Hide");
            ListHeaderCard.Header       = R.Get("Settings_DefaultListHeader");
            ListHeaderToggle.OnContent  = R.Get("Toggle_Show");
            ListHeaderToggle.OffContent = R.Get("Toggle_Hide");

            // Home Timeline Auto-Refresh (#207)
            HomeAutoLoadCard.Header      = R.Get("Settings_HomeAutoLoad");
            HomeAutoLoadCard.Description = R.Get("Settings_HomeAutoLoad_Description");
            HomeAutoLoadToggle.OnContent  = R.Get("Toggle_On");
            HomeAutoLoadToggle.OffContent = R.Get("Toggle_Off");
            HomeAutoLoadIntervalCard.Header      = R.Get("Settings_HomeAutoLoad_Interval");
            HomeAutoLoadIntervalCard.Description = R.Get("Settings_HomeAutoLoad_Interval_Description");

            // External Browser（試験機能から卒業した外部ブラウザー関連をすべて集約）
            var s = _parent?.Settings;

            ExternalBrowserHeader.Text = R.Get("Section_ExternalBrowser");

            OpenComposerCard.Header      = R.Get("Settings_OpenComposerInBrowser");
            OpenComposerCard.Description = R.Get("Settings_OpenComposerInBrowser_Description");
            OpenComposerToggle.OnContent  = R.Get("Toggle_On");
            OpenComposerToggle.OffContent = R.Get("Toggle_Off");

            OpenTimestampCard.Header      = R.Get("Settings_OpenTimestampInBrowser");
            OpenTimestampCard.Description = R.Get("Settings_OpenTimestampInBrowser_Description");
            OpenTimestampToggle.OnContent  = R.Get("Toggle_On");
            OpenTimestampToggle.OffContent = R.Get("Toggle_Off");

            BrowserCard.Header      = R.Get("Settings_ExternalBrowser");
            BrowserCard.Description = R.Get("Settings_ExternalBrowser_Description");
            BrowserCombo.ItemsSource = new List<string> { R.Get("Browser_System"), "Microsoft Edge" };

            // Edge Profile（ファイルシステム列挙に依存するためコードビハインドで構築）
            EdgeProfileCard.Header      = R.Get("Settings_EdgeProfile");
            EdgeProfileCard.Description = R.Get("Settings_EdgeProfile_Description");
            _edgeProfiles = EdgeService.EnumerateProfiles();

            if (_edgeProfiles.Count == 0)
            {
                EdgeProfileCombo.ItemsSource   = new List<string> { R.Get("Browser_EdgeNotFound") };
                EdgeProfileCombo.SelectedIndex = 0;
            }
            else
            {
                var names = new List<string>();
                int selectedIdx = 0;
                for (int i = 0; i < _edgeProfiles.Count; i++)
                {
                    var p = _edgeProfiles[i];
                    var detail = p.UserName.Length > 0 ? p.UserName : p.Directory;
                    names.Add($"{p.DisplayName}  ({detail})");
                    if (p.Directory == s?.EdgeProfileDirectory)
                        selectedIdx = i;
                }
                EdgeProfileCombo.ItemsSource   = names;
                EdgeProfileCombo.SelectedIndex = selectedIdx;
            }
            UpdateEdgeProfileComboEnabled();

            // ItemsSource 再設定で SelectedIndex が失われるため、バインディングを再評価して
            // ViewModel の値を反映し直す
            Bindings.Update();
        }

        private void EdgeProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_parent is null) return;

            var idx = EdgeProfileCombo.SelectedIndex;
            if (_edgeProfiles.Count == 0 || idx < 0 || idx >= _edgeProfiles.Count) return;

            // PopulateUI による再設定では値が変わらないため通知しない
            var dir = _edgeProfiles[idx].Directory;
            if (_parent.Settings.EdgeProfileDirectory == dir) return;

            _parent.Settings.EdgeProfileDirectory = dir;
            _parent.NotifySettingsChanged();
        }
    }
}
