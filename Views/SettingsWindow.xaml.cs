using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.UI;
using XTimelineViewer.Models;

namespace XTimelineViewer.Views
{
    public sealed partial class SettingsWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

        private readonly IntPtr _ownerHwnd;

        /// <summary>親ウィンドウから渡されたアプリ設定。ページが直接読み書きする。</summary>
        internal AppSettings Settings { get; }

        /// <summary>設定ファイルが格納されているフォルダーパス。</summary>
        internal string SettingsFolder { get; }

        /// <summary>設定が変更されたときに発火する。MainWindow が購読して保存・適用する。</summary>
        internal event Action? SettingsChanged;

        internal void NotifySettingsChanged() => SettingsChanged?.Invoke();

        /// <summary>ロード済み拡張機能の一覧。MainWindow が設定する。</summary>
        internal List<ExtensionInfo> Extensions { get; set; } = [];

        /// <summary>拡張機能の設定ダイアログを開くコールバック。MainWindow が提供する。</summary>
        internal Func<ExtensionInfo, Microsoft.UI.Xaml.XamlRoot, Task>? OpenExtensionSettingsAsync { get; set; }

        /// <summary>外部ブラウザー設定に従って URI を開くコールバック。MainWindow が提供する。</summary>
        internal Func<Uri, Task>? LaunchUriAsync { get; set; }

        /// <summary>プロファイル一覧。MainWindow が設定する。</summary>
        internal List<ProfileConfig> Profiles { get; set; } = [];

        /// <summary>バッジ色パレット。MainWindow が設定する。</summary>
        internal Color[] BadgeColors { get; set; } = [];

        /// <summary>プロファイル変更後の保存コールバック。</summary>
        internal Action? ProfilesModified { get; set; }

        /// <summary>プロファイル削除コールバック。</summary>
        internal Func<string, Task>? DeleteProfileAsync { get; set; }

        /// <summary>プロファイル作成コールバック。</summary>
        internal Action<ProfileConfig>? OnProfileCreated { get; set; }

        /// <summary>指定プロファイルが使用しているタイムライン数を返す。</summary>
        internal Func<string, int>? GetTimelineCount { get; set; }

        /// <summary>WebView2 ランタイムバージョン文字列。MainWindow が設定する。</summary>
        internal string EdgeVersion { get; set; } = "";

        /// <summary>winget が利用可能かどうか（unpackaged のみ意味がある）。</summary>
        internal bool HasWinget { get; set; }

        /// <summary>最新リリース情報を取得するコールバック。</summary>
        internal Func<Task<(Version version, string tag, string releaseUrl)>>? FetchLatestReleaseAsync { get; set; }

        /// <summary>バージョン比較で更新が利用可能かを判定するコールバック。</summary>
        internal Func<Version, Version, bool>? CheckIsUpdateAvailable { get; set; }

        /// <summary>設定のみ保存する（テーマ適用等はしない）コールバック。</summary>
        internal Action? SaveSettingsOnly { get; set; }

        /// <summary>メニューの更新バッジを更新するコールバック。</summary>
        internal Action? UpdateMenuBadge { get; set; }

        /// <summary>アプリを終了して winget でアップデートを開始するコールバック。</summary>
        internal Action? ExitAndRunWingetUpdate { get; set; }

        public SettingsWindow(IntPtr ownerHwnd, AppSettings settings, string settingsFolder)
        {
            _ownerHwnd = ownerHwnd;
            Settings = settings;
            SettingsFolder = settingsFolder;
            this.InitializeComponent();

            // ウィンドウサイズ・アイコン設定
            AppWindow.Resize(new SizeInt32(900, 620));
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);

            // ナビゲーション項目のテキストを設定
            RefreshNavText();

            // モーダル化: 親ウィンドウを無効化
            EnableWindow(_ownerHwnd, false);
            Closed += (_, _) => EnableWindow(_ownerHwnd, true);

            // 初期ページを選択
            NavView.SelectedItem = NavGeneral;
        }

        /// <summary>
        /// 親ウィンドウのテーマを設定ウィンドウにも適用する。
        /// </summary>
        public void ApplyTheme(ElementTheme theme)
        {
            ((FrameworkElement)Content).RequestedTheme = theme;
            MainWindow.ApplyTitleBarTheme(this, theme);
        }

        /// <summary>ナビゲーション項目と各ページのテキストを再設定する。</summary>
        internal void RefreshNavText()
        {
            Title                  = R.Get("AppSettings_Title");
            NavGeneral.Content     = R.Get("Nav_General");
            NavExperimental.Content = R.Get("Nav_Experimental");
            NavExtensions.Content  = R.Get("Nav_Extensions");
            NavProfiles.Content    = R.Get("Nav_Profiles");
            NavAbout.Content       = R.Get("Nav_About");
        }

        /// <summary>指定タグのナビゲーション項目を選択する。</summary>
        internal void SelectPage(string tag)
        {
            foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
            {
                if (item.Tag?.ToString() == tag)
                {
                    NavView.SelectedItem = item;
                    break;
                }
            }
        }

        /// <summary>設定ウィンドウの有効/無効を切り替える（子ウィンドウのモーダル化用）。</summary>
        internal void SetEnabled(bool enabled)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            EnableWindow(hwnd, enabled);
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item) return;
            var tag = item.Tag?.ToString();

            var pageType = tag switch
            {
                "General"      => typeof(Settings.GeneralPage),
                "Experimental" => typeof(Settings.ExperimentalPage),
                "Extensions"   => typeof(Settings.ExtensionsPage),
                "Profiles"     => typeof(Settings.ProfilesPage),
                "About"        => typeof(Settings.AboutPage),
                _              => null,
            };

            if (pageType is not null)
                ContentFrame.Navigate(pageType, this);
        }
    }
}
