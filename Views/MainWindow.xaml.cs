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

        internal static string GetExtensionsDir()
        {
            var sourceDir = Path.Combine(AppContext.BaseDirectory, "extensions");
            if (!PackageContext.IsPackaged) return sourceDir;

            var localDir = Path.Combine(
                Windows.Storage.ApplicationData.Current.LocalFolder.Path, "extensions");
            if (Directory.Exists(sourceDir))
            {
                // 新しい拡張機能があれば上書きコピー
                foreach (var src in Directory.GetDirectories(sourceDir))
                {
                    var dst = Path.Combine(localDir, Path.GetFileName(src));
                    CopyDirectory(src, dst);
                }
            }
            return localDir;
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.GetDirectories(src))
                CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
        }

        private AppSettings _appSettings = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private readonly List<TimelineConfig> _configs = [];
        private Grid? _draggingPane;
        private Grid? _focusedHeaderGrid;
        private readonly List<Action> _headerRefreshers = [];
        private readonly List<WebView2> _webViews = [];
        private bool _extensionsLoaded = false;
        private readonly List<ExtensionInfo> _loadedExtensions = [];
        // 環境そのものではなく「生成中の Task」をキャッシュする（#339）。
        // TryGetValue と await の間に隙間があると、同一プロファイルのペインを並行復元した
        // ときに同じ user data folder に対して CreateWithOptionsAsync が重複しうるため。
        private readonly Dictionary<string, Task<CoreWebView2Environment>> _profileEnvs = [];
        private List<ProfileConfig> _profiles = [];
        private readonly Dictionary<WebView2, Grid>            _webViewToPane  = [];
        private readonly Dictionary<Grid, Action>              _paneToSetFocus = [];
        // cfg.Url の変更をヘッダー（URL ラベル・種別アイコン・ホーム判定）へ反映する更新子 (#211)
        private readonly Dictionary<TimelineConfig, Action>    _paneUrlUpdaters = [];
        private readonly Dictionary<WebView2, DispatcherTimer>  _hardReloadTimers    = [];
        private readonly Dictionary<WebView2, DateTimeOffset>   _hardReloadStartTimes = [];
        private readonly Dictionary<WebView2, Action>           _hardReloadUiUpdaters = [];
        private readonly HashSet<WebView2>                       _pointerOverWebViews  = [];
        private readonly HashSet<WebView2>                       _urlDivergedWebViews  = [];
        private DispatcherTimer?  _hardReloadUiTimer;

        // ホーム自動更新（#207）のヘッダーインジケーター（ペイン → アイコン/ツールチップ）
        private readonly Dictionary<Grid, (FontIcon Icon, ToolTip Tip)> _autoLoadIndicators = [];

        // タイムライン番号バッジ（#225）。ペイン → 番号 TextBlock。表示順で 1..9 を振り直す。
        private readonly Dictionary<Grid, TextBlock> _paneNumberLabels = [];

        // 編集中（リプライ/引用）の WebView 集合（#258）。いずれかが編集中ならホーム自動更新を止める。
        private readonly HashSet<WebView2> _composingWebViews = [];

        // headerGrid → pane の対応（#227）。アクティブな headerGrid からペインを引くのに使う。
        private readonly Dictionary<Grid, Grid> _headerGridToPane = [];

        // 画像表示中のペインの一時拡大（試験機能 #287）。ペイン → 元の TimelineConfig（幅の復元用）。
        private readonly Dictionary<Grid, TimelineConfig> _paneToConfig = [];
        private Grid? _enlargedPane;

        // キーボードショートカット処理スクリプト（各 WebView2 に注入）
        private static readonly string KeyboardShortcutScript = """
            (function() {
                if (window._xtvKb) return;
                window._xtvKb = true;

                function addStyle() {
                    if (document.getElementById('xtv-kb-style')) return;
                    var s = document.createElement('style');
                    s.id = 'xtv-kb-style';
                    s.textContent = '.xtv-focused-post{outline:2px solid #0078D4!important;outline-offset:-2px!important;border-radius:4px!important;}';
                    (document.head || document.documentElement).appendChild(s);
                }
                document.readyState === 'loading'
                    ? document.addEventListener('DOMContentLoaded', addStyle)
                    : addStyle();

                var fi = -1;
                var getPosts = () => [...document.querySelectorAll('article[data-testid="tweet"]')];
                var isEdit   = () => { var el = document.activeElement; return el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable); };

                function navigatePosts(d) {
                    var ps = getPosts();
                    if (!ps.length) return;
                    ps.forEach(a => a.classList.remove('xtv-focused-post'));
                    fi = fi < 0 ? (d > 0 ? 0 : ps.length - 1)
                                : Math.max(0, Math.min(ps.length - 1, fi + d));
                    ps[fi]?.classList.add('xtv-focused-post');
                    ps[fi]?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
                }

                function actOnPost(id, alt) {
                    var ps = getPosts();
                    if (!ps.length) return;
                    var idx = fi;
                    if (idx < 0 || idx >= ps.length) {
                        // タイムラインでは Ctrl+↑/↓ での選択が必要（案C）。
                        // ただし個別ツイート（/status/）ページでは未選択でも主ツイート（先頭）に作用する（#254）。
                        if (/\/status\/\d+/.test(location.pathname)) idx = 0;
                        else return;
                    }
                    var b = ps[idx].querySelector('[data-testid="' + id + '"]' + (alt ? ',[data-testid="' + alt + '"]' : ''));
                    b?.click();
                }

                document.addEventListener('keydown', e => {
                    var c = e.ctrlKey, s = e.shiftKey, a = e.altKey, k = e.key, ni = !isEdit();
                    if (c && !s && !a) {
                        if (k === 'ArrowRight') { e.preventDefault(); window.chrome.webview.postMessage('focusNext'); return; }
                        if (k === 'ArrowLeft')  { e.preventDefault(); window.chrome.webview.postMessage('focusPrev'); return; }
                        if (k === 'n')          { e.preventDefault(); window.chrome.webview.postMessage('newPost');   return; }
                        if (k === 'ArrowUp')    { e.preventDefault(); navigatePosts(-1); return; }
                        if (k === 'ArrowDown')  { e.preventDefault(); navigatePosts(1);  return; }
                        if (k === 'f')          { e.preventDefault(); window.chrome.webview.postMessage('focusSearch'); return; }
                        if (k >= '1' && k <= '9') { e.preventDefault(); window.chrome.webview.postMessage('focusIndex:' + k); return; } // #225

                        if (k === 'r' && ni)    { e.preventDefault(); actOnPost('retweet',  'unretweet');      return; }
                        if (k === 'b' && ni)    { e.preventDefault(); actOnPost('bookmark', 'removeBookmark'); return; }
                        if (k === 'l' && ni)    { e.preventDefault(); actOnPost('like',     'unlike');         return; }
                    }
                    // Ctrl+Shift+←/→ でペインを左右へ並べ替え（#344）。
                    // 入力中は単語単位の選択を奪わないよう ni で除外する。
                    if (c && s && !a && ni) {
                        if (k === 'ArrowRight') { e.preventDefault(); window.chrome.webview.postMessage('movePaneNext'); return; }
                        if (k === 'ArrowLeft')  { e.preventDefault(); window.chrome.webview.postMessage('movePanePrev'); return; }
                    }
                    if (!c && !s && !a) {
                        if (k === 'F3')              { e.preventDefault(); window.chrome.webview.postMessage('focusSearch'); return; } // #228
                        if (k === 'Home'      && ni) { window.scrollTo({ top: 0, behavior: 'smooth' }); return; }
                        if (k === 'End'       && ni) { window.scrollTo({ top: document.documentElement.scrollHeight, behavior: 'smooth' }); return; }
                        if (k === 'F5')              { e.preventDefault(); location.reload(); return; }
                        if (k === 'Backspace' && ni) { e.preventDefault(); history.back(); return; }
                    }
                }, true);

                // マウスホイールでスクロールしたら、そのペインをアクティブ化する (#221)。
                // ホイールはキーフォーカスを移さないため、Home/End 等が別ペインに効いてしまうのを防ぐ。
                // 既にフォーカスがある（hasFocus）ときは何もしない。連打防止に 200ms スロットル。
                var lastAct = 0;
                document.addEventListener('wheel', function () {
                    var now = Date.now();
                    if (now - lastAct < 200) return;
                    lastAct = now;
                    if (!document.hasFocus()) window.chrome.webview.postMessage('activate');
                }, { passive: true, capture: true });
            })();
            """;

        private static readonly string TimestampInterceptScript = """
            (function() {
                if (window._xtvTimestamp) return;
                window._xtvTimestamp = true;
                document.addEventListener('click', function(e) {
                    if (!window._xtvOpenTimestampInBrowser) return;
                    var a = e.target.closest('a[href]');
                    if (!a || !a.querySelector('time')) return;
                    try {
                        var url = new URL(a.href);
                        if (/\/status\/\d+/.test(url.pathname)) {
                            e.preventDefault();
                            e.stopImmediatePropagation();
                            window.chrome.webview.postMessage('openTimestamp:' + url.href);
                        }
                    } catch(ex) {}
                }, true);
            })();
            """;

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
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
            Title = "XTimelineViewer (xTV)";
            RefreshUIText();
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
                foreach (var wv in _webViews.ToList())
                    CleanupWebView(wv);
            };
            ((FrameworkElement)Content).ActualThemeChanged += (s, e) => ApplyThemeToWebViews();
            LoadSettings();
            LoadProfiles();
            CleanupOrphanedProfiles();
            ApplySavedTheme();
            UpdateMenuUpdateBadge();
            _ = InitializeAsync();
            _ = CheckForUpdatesInBackgroundAsync();
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
