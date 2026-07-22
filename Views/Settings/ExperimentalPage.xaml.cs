using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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

            // 画像・GIF・動画をファイル保存（#299/#304/#308）。説明文は保存先フォルダーへの inline リンク付き（#312）
            VideoFrameSaveCard.Header      = R.Get("Settings_VideoFrameSave");
            VideoFrameSaveCard.Description = BuildVideoFrameSaveDescription();
            VideoFrameSaveToggle.OnContent  = R.Get("Toggle_On");
            VideoFrameSaveToggle.OffContent = R.Get("Toggle_Off");

            // ［…］メニューに「直前のリポストを検索」（#315）
            PriorRepostSearchCard.Header      = R.Get("Settings_PriorRepostSearch");
            PriorRepostSearchCard.Description = R.Get("Settings_PriorRepostSearch_Description");
            PriorRepostSearchToggle.OnContent  = R.Get("Toggle_On");
            PriorRepostSearchToggle.OffContent = R.Get("Toggle_Off");

            // ItemsSource 再設定で SelectedIndex が失われるため、バインディングを再評価する
            Bindings.Update();
        }

        // 保存先フォルダーへの inline リンクを含む説明文を組み立てる（#312）。
        // 3 行構成で、リンク（フォルダー名）は各行末に置き i18n の語順問題を軽減する。
        //   1) 画像の保存先 → ピクチャ\XTimelineViewer（リンク）
        //   2) GIF・動画の保存先 → ビデオ\XTimelineViewer（リンク）
        //   3) 動画はフレームキャプチャーで現在フレームも保存可
        private TextBlock BuildVideoFrameSaveDescription()
        {
            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Style        = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground   = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            };
            tb.Inlines.Add(new Run { Text = R.Get("Settings_VideoFrameSave_Desc1") });
            tb.Inlines.Add(MakeFolderLink(R.Get("Settings_VideoFrameSave_PicturesFolder"), isVideo: false));
            tb.Inlines.Add(new LineBreak());
            tb.Inlines.Add(new Run { Text = R.Get("Settings_VideoFrameSave_Desc2") });
            tb.Inlines.Add(MakeFolderLink(R.Get("Settings_VideoFrameSave_VideosFolder"), isVideo: true));
            tb.Inlines.Add(new LineBreak());
            tb.Inlines.Add(new Run { Text = R.Get("Settings_VideoFrameSave_Desc3") });
            return tb;
        }

        private static Hyperlink MakeFolderLink(string text, bool isVideo)
        {
            var link = new Hyperlink();
            link.Inlines.Add(new Run { Text = text });
            link.Click += (_, _) => OpenMediaFolder(isVideo);
            return link;
        }

        // 保存先フォルダー（動画/GIF=ビデオ、画像/フレーム=ピクチャ）を Explorer で開く（#312）。
        private static void OpenMediaFolder(bool isVideo)
        {
            try
            {
                var special = isVideo ? Environment.SpecialFolder.MyVideos : Environment.SpecialFolder.MyPictures;
                var dir = Path.Combine(Environment.GetFolderPath(special), "XTimelineViewer");
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch { /* フォルダーを開けなくても致命的ではない */ }
        }
    }
}
