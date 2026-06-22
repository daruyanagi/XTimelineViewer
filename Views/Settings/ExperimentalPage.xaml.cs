using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.ComponentModel;
using XTimelineViewer.ViewModels;

namespace XTimelineViewer.Views.Settings
{
    public sealed partial class ExperimentalPage : Page
    {
        private SettingsWindow? _parent;

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
            if (e.PropertyName == nameof(SettingsViewModel.LanguageIndex))
                PopulateUI();
        }

        private void PopulateUI()
        {
            PageTitle.Text = R.Get("Nav_Experimental");
            CautionBar.Message = R.Get("Experimental_Caution");  // #242

            // 投稿ウィンドウのプリロード（#244 案B）
            ComposePreloadCard.Header      = R.Get("Settings_ComposePreload");
            ComposePreloadCard.Description = R.Get("Settings_ComposePreload_Description");
            ComposePreloadToggle.OnContent  = R.Get("Toggle_On");
            ComposePreloadToggle.OffContent = R.Get("Toggle_Off");

            // #222 非推奨: 定期アクティブ化と関連オプションを無効化＋グレーアウト（v2.0 で削除予定）
            AutoActivateCard.Header      = R.Get("Settings_AutoActivate");
            AutoActivateCard.Description = R.Get("Settings_AutoActivate_Description") + "\n" + R.Get("Settings_Deprecated_Note");
            AutoActivateCard.IsEnabled   = false;

            ShowAutoActivateLabelCard.Header      = R.Get("Settings_ShowAutoActivateLabel");
            ShowAutoActivateLabelCard.Description = R.Get("Settings_ShowAutoActivateLabel_Description") + "\n" + R.Get("Settings_Deprecated_Note");
            ShowAutoActivateLabelToggle.OnContent  = R.Get("Toggle_On");
            ShowAutoActivateLabelToggle.OffContent = R.Get("Toggle_Off");
            ShowAutoActivateLabelCard.IsEnabled   = false;

            // ItemsSource 再設定で SelectedIndex が失われるため、バインディングを再評価する
            Bindings.Update();
        }
    }
}
