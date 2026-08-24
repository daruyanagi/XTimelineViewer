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

using XTimelineViewer.Services;

namespace XTimelineViewer.Views
{
    public sealed partial class SettingsWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

        private readonly IntPtr _ownerHwnd;

        /// <summary>親ウィンドウから渡されたアプリ設定。ページが直接読み書きする。</summary>
        internal AppSettings Settings { get; }

        /// <summary>設定ページが x:Bind する ViewModel (#199)。</summary>
        internal ViewModels.SettingsViewModel ViewModel { get; }

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

        /// <summary>最新バージョンを取得するコールバック（winget 版は winget、それ以外は GitHub Releases）。</summary>
        internal Func<Task<Version?>>? FetchLatestVersionAsync { get; set; }

        /// <summary>
        /// ZIP 版の自前更新を実行するコールバック（#328）。MainWindow が提供する。
        /// 展開まで済んだら true を返し、呼び出し元がアプリを終了する。
        /// </summary>
        internal Func<IProgress<double>, System.Threading.CancellationToken,
                      Task<Services.ZipUpdateRunner.RunResult>>? RunZipUpdateAsync { get; set; }

        /// <summary>アプリを終了するコールバック（更新の仕上げに使う）。</summary>
        internal Action? ExitApp { get; set; }

        /// <summary>拡張機能をプロファイル単位で有効・無効にする（#398）。</summary>
        internal Func<string, string, bool, Task>? SetExtensionEnabledAsync { get; set; }

        /// <summary>拡張機能をアンインストールする（#398）。成功したら true。</summary>
        internal Func<string, Task<bool>>? UninstallExtensionAsync { get; set; }

        /// <summary>新しく追加されたプロファイルでの既定を変える（#398）。</summary>
        internal Action<string, bool>? SetExtensionDefault { get; set; }

        /// <summary>拡張機能がこのプロファイルで有効か（#398）。</summary>
        internal Func<string, string, bool>? IsExtensionEnabled { get; set; }

        /// <summary>拡張機能の「新しいプロファイルでの既定」（#398）。</summary>
        internal Func<string, bool>? IsExtensionEnabledByDefault { get; set; }

        /// <summary>GitHub のリリースから候補を探す（#399）。</summary>
        internal Func<string, System.Threading.CancellationToken,
                      Task<(Services.ExtensionInstallRunner.Status, IReadOnlyList<Services.ExtensionInstaller.Candidate>)>>?
            FindExtensionCandidatesAsync { get; set; }

        /// <summary>取ってきて中身を確かめる（まだ入れない）（#399）。</summary>
        internal Func<Services.ExtensionInstaller.Candidate, IProgress<double>?, System.Threading.CancellationToken,
                      string?, Task<Services.ExtensionInstallRunner.Prepared>>?
            PrepareExtensionAsync { get; set; }

        /// <summary>一覧に出す入手先 URL（#404）。無ければ null。</summary>
        internal Func<string, string?, string?>? SourceUrlFor { get; set; }

        /// <summary>拡張機能に新しい版があるかを調べる（#406）。</summary>
        internal Func<string, System.Threading.CancellationToken, Task<(bool HasUpdate, string? Tag)>>?
            CheckExtensionUpdateAsync { get; set; }

        /// <summary>拡張機能を更新する（#406）。</summary>
        internal Func<string, System.Threading.CancellationToken, Task<bool>>? UpdateExtensionAsync { get; set; }

        /// <summary>確認が取れたものを入れる（#399）。</summary>
        internal Func<Services.ExtensionInstallRunner.Prepared, Task<bool>>? CommitExtensionAsync { get; set; }

        /// <summary>設定のみ保存する（テーマ適用等はしない）コールバック。</summary>
        internal Action? SaveSettingsOnly { get; set; }

        /// <summary>メニューの更新バッジを更新するコールバック。</summary>
        internal Action? UpdateMenuBadge { get; set; }

        /// <summary>
        /// winget に更新を委ねてアプリを終了するコールバック。
        /// winget を起こせなかったときは終了せず false を返す（#412）。
        /// </summary>
        internal Func<bool>? ExitAndRunWingetUpdate { get; set; }

        public SettingsWindow(IntPtr ownerHwnd, AppSettings settings, string settingsFolder)
        {
            _ownerHwnd = ownerHwnd;
            Settings = settings;
            SettingsFolder = settingsFolder;
            ViewModel = new ViewModels.SettingsViewModel(settings, NotifySettingsChanged);

            // テーマ変更は設定ウィンドウ自身にも即時反映する
            ViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(ViewModels.SettingsViewModel.ThemeIndex)) return;
                ApplyTheme(Settings.Theme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark"  => ElementTheme.Dark,
                    _       => ElementTheme.Default,
                });
            };

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
            NavUserInterface.Content = R.Get("Nav_UserInterface");
            NavData.Content        = R.Get("Nav_Data");
            NavExperimental.Content = R.Get("Nav_Experimental");
            NavExtensions.Content  = R.Get("Nav_Extensions");
            NavProfiles.Content    = R.Get("Nav_Profiles");
            NavAbout.Content       = R.Get("Nav_About");
        }

        /// <summary>
        /// 指定タグのナビゲーション項目を選択する。
        ///
        /// フッター側も探すこと（#392）。「バージョン情報」は
        /// FooterMenuItems にあるため、MenuItems だけ走査していた頃は
        /// <b>黙って何もしない</b>状態だった。
        /// </summary>
        internal void SelectPage(string tag)
        {
            var item = NavView.MenuItems.OfType<NavigationViewItem>()
                       .Concat(NavView.FooterMenuItems.OfType<NavigationViewItem>())
                       .FirstOrDefault(i => i.Tag?.ToString() == tag);

            if (item is not null) NavView.SelectedItem = item;
            else AppLog.Debug($"SettingsWindow: 選択できないページを指定された: {tag}");
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item) return;
            var tag = item.Tag?.ToString();

            var pageType = tag switch
            {
                "General"      => typeof(Settings.GeneralPage),
                "UserInterface" => typeof(Settings.UserInterfacePage),
                "Data"         => typeof(Settings.UserDataPage),
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
