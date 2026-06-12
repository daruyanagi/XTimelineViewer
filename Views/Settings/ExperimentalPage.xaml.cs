using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;
using System.ComponentModel;
using XTimelineViewer.Services;
using XTimelineViewer.ViewModels;

namespace XTimelineViewer.Views.Settings
{
    public sealed partial class ExperimentalPage : Page
    {
        private SettingsWindow? _parent;
        private List<EdgeProfile> _edgeProfiles = [];

        /// <summary>x:Bind のバインディングソース。XAML から参照される。</summary>
        public SettingsViewModel? VM { get; private set; }

        public ExperimentalPage()
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
                case nameof(SettingsViewModel.IsEdgeSelected):
                    UpdateEdgeProfileComboEnabled();
                    break;
            }
        }

        private void UpdateEdgeProfileComboEnabled()
            => EdgeProfileCombo.IsEnabled = (VM?.IsEdgeSelected ?? false) && _edgeProfiles.Count > 0;

        private void PopulateUI()
        {
            var s = _parent?.Settings;

            PageTitle.Text = R.Get("Nav_Experimental");

            OpenComposerCard.Header      = R.Get("Settings_OpenComposerInBrowser");
            OpenComposerCard.Description = R.Get("Settings_OpenComposerInBrowser_Description");
            OpenComposerToggle.OnContent  = R.Get("Toggle_On");
            OpenComposerToggle.OffContent = R.Get("Toggle_Off");

            OpenTimestampCard.Header      = R.Get("Settings_OpenTimestampInBrowser");
            OpenTimestampCard.Description = R.Get("Settings_OpenTimestampInBrowser_Description");
            OpenTimestampToggle.OnContent  = R.Get("Toggle_On");
            OpenTimestampToggle.OffContent = R.Get("Toggle_Off");

            AutoActivateCard.Header      = R.Get("Settings_AutoActivate");
            AutoActivateCard.Description = R.Get("Settings_AutoActivate_Description");

            ShowAutoActivateLabelCard.Header      = R.Get("Settings_ShowAutoActivateLabel");
            ShowAutoActivateLabelCard.Description = R.Get("Settings_ShowAutoActivateLabel_Description");
            ShowAutoActivateLabelToggle.OnContent  = R.Get("Toggle_On");
            ShowAutoActivateLabelToggle.OffContent = R.Get("Toggle_Off");

            ExternalBrowserHeader.Text = R.Get("Section_ExternalBrowser");

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

            // ItemsSource 再設定で SelectedIndex が失われるため、バインディングを再評価する
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
