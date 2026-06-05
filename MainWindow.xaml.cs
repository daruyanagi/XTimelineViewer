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
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.UI;
using XTimelineViewer.ViewModels;

namespace XTimelineViewer
{
    public sealed partial class MainWindow : Window
    {
        /// <summary>x:Bind のバインディングソース。XAML から参照される。</summary>
        public MainWindowViewModel ViewModel { get; } = new();

        private static readonly string SaveFilePath      = GetDataFilePath("timelines.json");
        private static readonly string SettingsFilePath  = GetDataFilePath("settings.json");
        private static readonly string ProfilesFilePath  = GetDataFilePath("profiles.json");

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

        private static string GetExtensionsDir()
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
        private readonly Dictionary<string, CoreWebView2Environment> _profileEnvs = [];
        private CoreWebView2Environment? _composeEnv;
        private List<ProfileConfig> _profiles = [];
        private readonly Dictionary<WebView2, Grid>            _webViewToPane  = [];
        private readonly Dictionary<Grid, Action>              _paneToSetFocus = [];
        private readonly Dictionary<WebView2, DispatcherTimer>  _hardReloadTimers    = [];
        private readonly Dictionary<WebView2, DateTimeOffset>   _hardReloadStartTimes = [];
        private readonly Dictionary<WebView2, Action>           _hardReloadUiUpdaters = [];
        private readonly HashSet<WebView2>                       _pointerOverWebViews  = [];
        private readonly HashSet<WebView2>                       _urlDivergedWebViews  = [];
        private DispatcherTimer?  _hardReloadUiTimer;
        private DispatcherTimer?  _autoActivateTimer;
        private int               _dialogOpenCount      = 0;
        private DateTimeOffset    _autoActivateStartTime;
        private readonly HashSet<Grid> _homeHeaderGrids = [];

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
                    if (fi < 0 || fi >= ps.length) return;
                    var b = ps[fi].querySelector('[data-testid="' + id + '"]' + (alt ? ',[data-testid="' + alt + '"]' : ''));
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
                        if (k === 'r' && ni)    { e.preventDefault(); actOnPost('retweet',  'unretweet');      return; }
                        if (k === 'b' && ni)    { e.preventDefault(); actOnPost('bookmark', 'removeBookmark'); return; }
                        if (k === 'f' && ni)    { e.preventDefault(); actOnPost('like',     'unlike');         return; }
                    }
                    if (!c && !s && !a) {
                        if (k === 'Home'      && ni) { window.scrollTo({ top: 0, behavior: 'smooth' }); return; }
                        if (k === 'End'       && ni) { window.scrollTo({ top: document.documentElement.scrollHeight, behavior: 'smooth' }); return; }
                        if (k === 'F5')              { e.preventDefault(); location.reload(); return; }
                        if (k === 'Backspace' && ni) { e.preventDefault(); history.back(); return; }
                    }
                }, true);
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

        private static string GetProfilesDataDir()
        {
            if (PackageContext.IsPackaged)
                return Path.Combine(
                    Windows.Storage.ApplicationData.Current.LocalFolder.Path, "profiles");
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XTimelineViewer", "profiles");
        }

        private async Task<CoreWebView2Environment> GetOrCreateProfileEnvAsync(string profileId)
        {
            if (_profileEnvs.TryGetValue(profileId, out var cached)) return cached;
            var userDataFolder = profileId == "default"
                ? ""
                : Path.Combine(GetProfilesDataDir(), profileId);
            if (userDataFolder.Length > 0)
                Directory.CreateDirectory(userDataFolder);
            var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true };
            var env = await CoreWebView2Environment.CreateWithOptionsAsync(
                "", userDataFolder, options);
            _profileEnvs[profileId] = env;
            Debug.WriteLine($"[Profile] Env created: profileId={profileId}, UserDataFolder={env.UserDataFolder}");
            return env;
        }

        private static readonly string ComposeUserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XTimelineViewer", "compose-profile");

        private async Task<CoreWebView2Environment> GetOrCreateComposeEnvAsync()
        {
            if (_composeEnv is not null) return _composeEnv;
            var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = false };
            _composeEnv = await CoreWebView2Environment.CreateWithOptionsAsync(
                "", userDataFolder: ComposeUserDataFolder, options);
            return _composeEnv;
        }

        public MainWindow()
        {
            this.InitializeComponent();
            AppWindow.Resize(new SizeInt32(1400, 900));
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
            Title = "XTimelineViewer";
            RefreshUIText();
            Closed += async (s, e) =>
            {
                _hardReloadUiTimer?.Stop();
                _autoActivateTimer?.Stop();
                foreach (var wv in _webViews.ToList())
                    CleanupWebView(wv);
                await SaveTimelinesAsync();
            };
            ((FrameworkElement)Content).ActualThemeChanged += (s, e) => ApplyThemeToWebViews();
            LoadSettings();
            LoadProfiles();
            CleanupOrphanedProfiles();
            ApplySavedTheme();
            ApplyAutoActivateTimer();
            UpdateMenuUpdateBadge();
            _ = RestoreTimelinesAsync();
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
            ManageProfilesMenuItem.Text = R.Get("Menu_ManageProfiles");
            AppSettingsMenuItem.Text    = R.Get("Menu_Settings");
            AboutMenuItem.Text          = R.Get("Menu_About");

            AddTimelineSubMenu.Text           = R.Get("Menu_AddTimeline");
            AddHomeTimelineItem.Text          = R.Get("Timeline_Home");
            AddNotificationsTimelineItem.Text = R.Get("Timeline_Notifications");
            AddBookmarksTimelineItem.Text     = R.Get("Timeline_Bookmarks");
            // アイコンは既存ペインと同じく URL 種別から導出して一貫性を保つ
            AddHomeIcon.Glyph          = GetTimelineGlyph(HomeTimelineUrl);
            AddNotificationsIcon.Glyph = GetTimelineGlyph(NotificationsTimelineUrl);
            AddBookmarksIcon.Glyph     = GetTimelineGlyph(BookmarksTimelineUrl);
        }

        private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dlg)
        {
            _dialogOpenCount++;
            try   { return await dlg.ShowAsync(); }
            finally
            {
                _dialogOpenCount--;
                if (_dialogOpenCount == 0) RestartAutoActivateTimer();
            }
        }

        private static bool IsOnBaseUrl(string currentUrl, string baseUrl)
        {
            if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var cur))  return false;
            if (!Uri.TryCreate(baseUrl,    UriKind.Absolute, out var @base)) return false;
            return string.Equals(cur.Host,         @base.Host,         StringComparison.OrdinalIgnoreCase)
                && string.Equals(cur.AbsolutePath, @base.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
