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

            // 投稿後にプライマリプロフィールへ戻す（#285）
            ComposeResetToPrimaryCard.Header      = R.Get("Settings_ComposeResetToPrimary");
            ComposeResetToPrimaryCard.Description = R.Get("Settings_ComposeResetToPrimary_Description");
            ComposeResetToPrimaryToggle.OnContent  = R.Get("Toggle_On");
            ComposeResetToPrimaryToggle.OffContent = R.Get("Toggle_Off");

            // 画像表示中のペインを一時拡大（#287）
            MediaEnlargeCard.Header      = R.Get("Settings_MediaEnlarge");
            MediaEnlargeCard.Description = R.Get("Settings_MediaEnlarge_Description");
            MediaEnlargeToggle.OnContent  = R.Get("Toggle_On");
            MediaEnlargeToggle.OffContent = R.Get("Toggle_Off");

            VideoEnlargeCard.Header      = R.Get("Settings_VideoEnlarge");
            VideoEnlargeCard.Description = R.Get("Settings_VideoEnlarge_Description");
            VideoEnlargeToggle.OnContent  = R.Get("Toggle_On");
            VideoEnlargeToggle.OffContent = R.Get("Toggle_Off");

            MediaOverlayButtonCard.Header      = R.Get("Settings_MediaOverlayButton");
            MediaOverlayButtonCard.Description = R.Get("Settings_MediaOverlayButton_Description");
            MediaOverlayButtonToggle.OnContent  = R.Get("Toggle_On");
            MediaOverlayButtonToggle.OffContent = R.Get("Toggle_Off");

            // ItemsSource 再設定で SelectedIndex が失われるため、バインディングを再評価する
            Bindings.Update();
        }
    }
}
