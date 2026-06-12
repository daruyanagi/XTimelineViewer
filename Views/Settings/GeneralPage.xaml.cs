using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;
using System.ComponentModel;
using XTimelineViewer.ViewModels;

namespace XTimelineViewer.Views.Settings
{
    public sealed partial class GeneralPage : Page
    {
        private SettingsWindow? _parent;

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
            // 言語変更時はこのページ自身の表示も新しい言語で再構築する
            if (e.PropertyName == nameof(SettingsViewModel.LanguageIndex))
                PopulateUI();
        }

        private void PopulateUI()
        {
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

            // Language
            LanguageCard.Header      = R.Get("Settings_Language");
            LanguageCard.Description = R.Get("Settings_Language_Description");
            LanguageCombo.ItemsSource = new List<string>
            {
                R.Get("Language_System"),
                R.Get("Language_JA"),
                R.Get("Language_EN"),
            };

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

            // ItemsSource 再設定で SelectedIndex が失われるため、バインディングを再評価して
            // ViewModel の値を反映し直す
            Bindings.Update();
        }
    }
}
