using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.UI;
using XTimelineViewer.Models;
using XTimelineViewer.Services;
using XTimelineViewer.ViewModels;

using XTimelineViewer.Views.Controls;

namespace XTimelineViewer.Views
{
    public sealed partial class MainWindow : Window
    {
        /// <summary>x:Bind のバインディングソース。XAML から参照される。</summary>
        public MainWindowViewModel ViewModel { get; } = new();

        private static readonly string SaveFilePath      = GetDataFilePath("timelines.json");
        private static readonly string SettingsFilePath  = GetDataFilePath("settings.json");
        private static readonly string ProfilesFilePath  = GetDataFilePath("profiles.json");

        // 終了時に一度だけ保存してから閉じ直すためのフラグ（#338）
        private bool _closeHandled;

        // MSIX パッケージ環境では ApplicationData.Current.LocalFolder を使用する。
        // 旧バージョン（アンパッケージド）からの移行のため、旧パスにファイルが存在すれば自動コピーする。
        private static string GetDataFilePath(string filename)
        {
            if (!PackageContext.IsPackaged)
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "XTimelineViewer", filename);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                return path;
            }

            var newPath = Path.Combine(
                Windows.Storage.ApplicationData.Current.LocalFolder.Path, filename);

            if (!File.Exists(newPath))
            {
                var oldPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "XTimelineViewer", filename);
                if (File.Exists(oldPath))
                    File.Copy(oldPath, newPath);
            }
            return newPath;
        }

        /// <summary>
        /// 拡張機能の置き場（#396）。
        ///
        /// 以前はインストール先の中を直接使っていたが、更新はインストール先ごと
        /// 置き換えるため<b>利用者が入れた拡張機能が消えていた</b>。設定やプロファイルと
        /// 同じく、アプリ本体と独立した場所に置く。
        ///
        /// 旧い場所に残っているものは初回に移す。パッケージ版の WindowsApps 配下は
        /// 書き込めないので、そちらは複製にとどめる。
        /// </summary>
        internal static string GetExtensionsDir()
        {
            var newDir = PackageContext.IsPackaged
                ? Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "extensions")
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "XTimelineViewer", "extensions");

            var oldDir = Path.Combine(AppContext.BaseDirectory, "extensions");
            var moved  = ExtensionStore.Migrate(oldDir, newDir, copyOnly: PackageContext.IsPackaged);
            if (moved > 0) AppLog.Debug($"GetExtensionsDir: {moved} 件を {newDir} へ移行した");

            Directory.CreateDirectory(newDir);
            return newDir;
        }

        private AppSettings _appSettings = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private readonly List<TimelineConfig> _configs = [];
        /// <summary>
        /// 表示順のペイン一覧。以前は_webViews や _autoLoadIndicators を
        /// 「全ペインの代用」にして列挙していた（#345）。
        /// </summary>
        private IEnumerable<TimelinePane> Panes => TimelinePanel.Children.OfType<TimelinePane>();

        /// <summary>WebView2 からそのペインを引く。ペインは多くても数枚なので線形探索で十分。</summary>
        private TimelinePane? PaneOf(WebView2 webView) => Panes.FirstOrDefault(p => p.WebView == webView);

        private TimelinePane? _draggingPane;
        private TimelinePane? _focusedPane;
        // ペイン → ヘッダーの配色を再適用する処理。
        // 以前は List<Action> だったが、除去が参照一致になるため
        // デリゲート実体を持たない削除経路からは掃除できなかった（#362）。
        // 拡張機能を読み込み済みのプロファイル（#397）。
        // 以前は bool 1 つで、最初に作られた WebView2 のプロファイルにしか
        // 入らなかった。拡張機能は CoreWebView2Profile 単位で登録される。
        private readonly HashSet<string> _extensionsLoadedProfiles = [];

        // 一覧とツールバーへ出した拡張機能（#397）。
        // 読み込みはプロファイルごとに走るので、これが無いと同じ拡張機能の
        // ボタンがプロファイルの数だけ並んでしまう。
        private readonly HashSet<string> _surfacedExtensionIds = [];

        // 読み込みに失敗して既に知らせた拡張機能（#397）。
        // 読み込みがプロファイルごとに走るので、これが無いと
        // 同じ失敗でダイアログがプロファイルの数だけ出る。
        private readonly HashSet<string> _reportedExtensionErrors =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ExtensionInfo> _loadedExtensions = [];
        // 環境そのものではなく「生成中の Task」をキャッシュする（#339）。
        // TryGetValue と await の間に隙間があると、同一プロファイルのペインを並行復元した
        // ときに同じ user data folder に対して CreateWithOptionsAsync が重複しうるため。
        private readonly Dictionary<string, Task<CoreWebView2Environment>> _profileEnvs = [];
        private List<ProfileConfig> _profiles = [];
        // cfg.Url の変更をヘッダー（URL ラベル・種別アイコン・ホーム判定）へ反映する更新子 (#211)
        private readonly Dictionary<WebView2, DispatcherTimer>  _hardReloadTimers    = [];
        private readonly Dictionary<WebView2, DateTimeOffset>   _hardReloadStartTimes = [];
        private readonly Dictionary<WebView2, Action>           _hardReloadUiUpdaters = [];
        private readonly HashSet<WebView2>                       _pointerOverWebViews  = [];
        private readonly HashSet<WebView2>                       _urlDivergedWebViews  = [];
        private DispatcherTimer?  _hardReloadUiTimer;

        // ホーム自動更新（#207）のヘッダーインジケーター（ペイン → アイコン/ツールチップ）


        // タイムライン番号バッジ（#225）。ペイン → 番号 TextBlock。表示順で 1..9 を振り直す。


        // 編集中（リプライ/引用）の WebView 集合（#258）。いずれかが編集中ならホーム自動更新を止める。
        private readonly HashSet<WebView2> _composingWebViews = [];

        // headerGrid → pane の対応（#227）。アクティブな headerGrid からペインを引くのに使う。

        // 画像表示中のペインの一時拡大（試験機能 #287）。ペイン → 元の TimelineConfig（幅の復元用）。

        private TimelinePane? _enlargedPane;

        // キーボードショートカット処理スクリプト（各 WebView2 に注入）
        private static readonly string KeyboardShortcutScript = ScriptLoader.Get("KeyboardShortcut");

        private static readonly string TimestampInterceptScript = ScriptLoader.Get("TimestampIntercept");

        // プロファイルデータの保存先は ProfileService に共通化済み (#157)
        private static string GetProfilesDataDir() => ProfileService.GetProfilesDataDir();

        private Task<CoreWebView2Environment> GetOrCreateProfileEnvAsync(string profileId)
        {
            // 生成 Task を先に登録してから await させることで、並行呼び出しでも生成は 1 回になる。
            // UI スレッドから呼ばれる前提なので Dictionary のままでよい。
            if (_profileEnvs.TryGetValue(profileId, out var cached)) return cached;

            var task = CreateProfileEnvAsync(profileId);
            _profileEnvs[profileId] = task;

            // 失敗した Task を残すと以後ずっと同じ例外を返すため、キャッシュから外して再試行できるようにする。
            _ = task.ContinueWith(
                t =>
                {
                    if (_profileEnvs.TryGetValue(profileId, out var current) && current == t)
                        _profileEnvs.Remove(profileId);
                    LogError($"CreateProfileEnv (profileId={profileId})", t.Exception!);
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.FromCurrentSynchronizationContext());

            return task;
        }

        private static async Task<CoreWebView2Environment> CreateProfileEnvAsync(string profileId)
        {
            var userDataFolder = profileId == "default"
                ? ""
                : Path.Combine(GetProfilesDataDir(), profileId);
            if (userDataFolder.Length > 0)
                Directory.CreateDirectory(userDataFolder);
            var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true };
            var env = await CoreWebView2Environment.CreateWithOptionsAsync(
                "", userDataFolder, options);
            Debug.WriteLine($"[Profile] Env created: profileId={profileId}, UserDataFolder={env.UserDataFolder}");
            return env;
        }

        public MainWindow()
        {
            this.InitializeComponent();
            AppWindow.Resize(new SizeInt32(1400, 900));
            // ツールバーが重なるほど狭くできないよう下限を引く（#342）
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth  = 480;
                presenter.PreferredMinimumHeight = 400;
            }
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
            Title = "XTimelineViewer (xTV)";
            RefreshUIText();
            HookContrastThemeChanges();  // コントラストテーマの切り替えに追従（#341）
            // 終了時の保存は Closing 側で行う（#338）。
            // Closed は async void 相当で、await の後を待たずにプロセスが終了しうるため、
            // 直前の変更（ペインの追加・並べ替えなど）が取りこぼされることがあった。
            // ここでは一度クローズをキャンセルして保存を待ち、完了後に閉じ直す。
            AppWindow.Closing += async (sender, args) =>
            {
                if (_closeHandled) return;   // 保存後の閉じ直しでは素通しする
                args.Cancel = true;

                _closeHandled = true;
                try
                {
                    await SaveTimelinesAsync();
                }
                catch (Exception ex)
                {
                    LogError("AppWindow.Closing (save)", ex);
                }
                Close();
            };

            Closed += (s, e) =>
            {
                _hardReloadUiTimer?.Stop();
                DisposeComposeWarm();  // 投稿プリロードの後始末（#244 案B）
                foreach (var wv in Panes.Select(p => p.WebView).ToList())
                    CleanupWebView(wv);
            };
            ((FrameworkElement)Content).ActualThemeChanged += (s, e) => ApplyThemeToWebViews();
            LoadSettings();
            LoadProfiles();
            CleanupOrphanedProfiles();
            ApplySavedTheme();
            UpdateMenuUpdateBadge();
            InitializeAsync().FireAndForget(nameof(InitializeAsync));
            CheckForUpdatesInBackgroundAsync().FireAndForget(nameof(CheckForUpdatesInBackgroundAsync));
        }

        // ツールバー・メニューなど常駐 UI の静的テキストを現在の言語で再適用する。
        // 起動時のほか、言語切り替え後（#117）にも呼ばれる。
        private void RefreshUIText()
        {
            PostLabel.Text        = R.Get("PostLabel.Text");
            DropHintTitle.Text    = R.Get("DropHintTitle.Text");
            DropHintSubtitle.Text = R.Get("DropHintSubtitle.Text");
            ToolTipService.SetToolTip(PostBtn,    R.Get("PostBtn_Tooltip"));
            ToolTipService.SetToolTip(AppMenuBtn, R.Get("AppMenu_Tooltip"));
            AutomationProperties.SetName(PostBtn,    R.Get("PostBtn_Tooltip"));
            AutomationProperties.SetName(AppMenuBtn, R.Get("AppMenu_Tooltip"));
            ThemeSubMenu.Text       = R.Get("Menu_Theme");
            ThemeSystemItem.Text    = R.Get("Theme_System");
            ThemeLightItem.Text     = R.Get("Theme_Light");
            ThemeDarkItem.Text      = R.Get("Theme_Dark");
            UpdateThemeRadioState();
            AppSettingsMenuItem.Text = R.Get("Menu_Settings");

            NewProfileMenuItem.Text           = R.Get("Menu_NewProfile");
            AddTimelineSubMenu.Text           = R.Get("Menu_AddTimeline");
            AddHomeTimelineItem.Text          = R.Get("Timeline_Home");
            AddNotificationsTimelineItem.Text = R.Get("Timeline_Notifications");
            AddBookmarksTimelineItem.Text     = R.Get("Timeline_Bookmarks");
            AddListsTimelineItem.Text         = R.Get("Timeline_Lists");
            // アイコンは既存ペインと同じく URL 種別から導出して一貫性を保つ
            AddHomeIcon.Glyph          = UrlHelper.GetTimelineGlyph(HomeTimelineUrl);
            AddNotificationsIcon.Glyph = UrlHelper.GetTimelineGlyph(NotificationsTimelineUrl);
            AddBookmarksIcon.Glyph     = UrlHelper.GetTimelineGlyph(BookmarksTimelineUrl);
            // リスト URL はハンドル依存のため、アイコン導出には代表的な一覧パスを使う
            AddListsIcon.Glyph         = UrlHelper.GetTimelineGlyph(BuildListsUrl("_"));

            SearchBox.PlaceholderText = R.Get("Search_Placeholder");
            ToolTipService.SetToolTip(SearchBox, R.Get("Search_Tooltip"));
            AutomationProperties.SetName(SearchBox, R.Get("Search_Tooltip"));
        }

        private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dlg)
        {
            // すべてのダイアログに現在のテーマを自動適用して設定漏れを防ぐ (#126)
            dlg.RequestedTheme = ((FrameworkElement)Content).ActualTheme;
            return await dlg.ShowAsync();
        }

        /// <summary>
        /// 外部ブラウザー設定に応じて URI を開く。
        /// Edge プロファイル指定が有効かつ http/https の場合は Edge で開き、
        /// それ以外はシステム既定に委ねる。
        /// </summary>
        private async Task LaunchUriByEdgeProfileAsync(Uri uri)
        {
            if (_appSettings.ExternalBrowser == "edge" &&
                (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                var edgePath = EdgeService.FindEdgePath();
                if (edgePath is not null)
                {
                    EdgeService.LaunchInProfile(edgePath, _appSettings.EdgeProfileDirectory, uri);
                    return;
                }
                // Edge が見つからない場合はシステム既定にフォールバック
                Debug.WriteLine("[Edge] Falling back to system default — Edge not found");
            }

            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }
}
