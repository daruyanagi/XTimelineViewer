using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.UI;

namespace XTimelineViewer
{
    internal class TimelineConfig
    {
        public string Url        { get; set; } = "";
        public double Width      { get; set; } = 350;
        public bool   HideHeader  { get; set; } = false;
        public bool   HideCompose { get; set; } = true;
    }

    internal class AppSettings
    {
        public bool   SeparateComposeEnv    { get; set; } = false;
        public bool   OpenComposerInBrowser     { get; set; } = false;
        public bool   OpenTweetInBrowser    { get; set; } = false;
        public string Theme                 { get; set; } = "Default"; // "Light" | "Dark" | "Default"
        public int    AutoActivateMinutes   { get; set; } = 0;         // 0 = 無効
    }

    public sealed partial class MainWindow : Window
    {
        private static readonly string SaveFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XTimelineViewer", "timelines.json");
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XTimelineViewer", "settings.json");
        private AppSettings _appSettings = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private readonly List<TimelineConfig> _configs = [];
        private Grid? _draggingPane;
        private Grid? _focusedHeaderGrid;
        private readonly List<Action> _headerRefreshers = [];
        private readonly List<WebView2> _webViews = [];
        private bool _extensionsLoaded = false;
        private CoreWebView2Environment? _webViewEnv;
        private CoreWebView2Environment? _composeEnv;
        private readonly Dictionary<WebView2, Grid> _webViewToPane  = [];
        private readonly Dictionary<Grid, Action>   _paneToSetFocus = [];
        private DispatcherTimer? _autoActivateTimer;

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

        // ツイート permalink 遷移を外部ブラウザーへ転送するスクリプト。
        // 2層構成:
        //   1. capture 相クリック横取り: <a href="/status/"> を React より先にブロック
        //   2. pushState 横取り: React onClick 経由の SPA 遷移を orig + back() で戻す
        // window._xtvOpenTweetInBrowser を ExecuteScriptAsync で切り替えて有効/無効を制御する。
        private static readonly string TweetInterceptScript = """
            (function() {
                if (window._xtvTweet) return;
                window._xtvTweet = true;

                // Layer 1: <a href> クリックを capture 最優先で横取り
                document.addEventListener('click', function(e) {
                    if (!window._xtvOpenTweetInBrowser) return;
                    var a = e.target.closest('a[href]');
                    if (!a) return;
                    try {
                        var url = new URL(a.href);
                        if (/\/status\/\d+/.test(url.pathname)) {
                            e.preventDefault();
                            e.stopImmediatePropagation();
                            window.chrome.webview.postMessage('openTweet:' + url.href);
                        }
                    } catch(ex) {}
                }, true);

                // Layer 2: React onClick 経由の pushState を横取りして即 back()
                var orig = history.pushState.bind(history);
                history.pushState = function(state, title, url) {
                    if (window._xtvOpenTweetInBrowser && url) {
                        var s = url.toString();
                        if (/\/status\/\d+/.test(s)) {
                            try {
                                var abs = new URL(s, location.origin).href;
                                window.chrome.webview.postMessage('openTweet:' + abs);
                            } catch(ex) {}
                            orig(state, title, url);
                            setTimeout(function() { history.back(); }, 0);
                            return;
                        }
                    }
                    orig(state, title, url);
                };
            })();
            """;

        private static readonly string EdgeDevAppDir =
            @"C:\Program Files (x86)\Microsoft\Edge Dev\Application";

        private static string? FindEdgeDevVersionFolder()
        {
            if (!Directory.Exists(EdgeDevAppDir)) return null;
            return Directory.GetDirectories(EdgeDevAppDir)
                .Where(d => Version.TryParse(Path.GetFileName(d), out _))
                .OrderByDescending(d => Version.Parse(Path.GetFileName(d)))
                .FirstOrDefault();
        }

        private async Task<CoreWebView2Environment> GetOrCreateEnvAsync()
        {
            if (_webViewEnv is not null) return _webViewEnv;
            var versionFolder = FindEdgeDevVersionFolder();
            var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true };
            _webViewEnv = await CoreWebView2Environment.CreateWithOptionsAsync(
                versionFolder ?? "", userDataFolder: "", options);
            return _webViewEnv;
        }

        private static readonly string ComposeUserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XTimelineViewer", "compose-profile");

        private async Task<CoreWebView2Environment> GetOrCreateComposeEnvAsync()
        {
            if (_composeEnv is not null) return _composeEnv;
            var versionFolder = FindEdgeDevVersionFolder();
            var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = false };
            _composeEnv = await CoreWebView2Environment.CreateWithOptionsAsync(
                versionFolder ?? "", userDataFolder: ComposeUserDataFolder, options);
            return _composeEnv;
        }

        public MainWindow()
        {
            this.InitializeComponent();
            AppWindow.Resize(new SizeInt32(1400, 900));
            Title = $"XTimelineViewer — {SaveFilePath}";
            Closed += async (s, e) => await SaveTimelinesAsync();
            ((FrameworkElement)Content).ActualThemeChanged += (s, e) => ApplyThemeToWebViews();
            LoadSettings();
            ApplySavedTheme();
            ApplyAutoActivateTimer();
            _ = RestoreTimelinesAsync();
        }

        // ── App settings ──────────────────────────────────────────────────────

        private void LoadSettings()
        {
            try
            {
                var json = File.ReadAllText(SettingsFilePath);
                _appSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
            }
            catch { /* ファイルが存在しない場合などは無視 */ }
            _appSettings.SeparateComposeEnv = false; // 廃止予定: 強制無効化 (#17)
        }

        private void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(_appSettings, JsonOptions));
        }

        private void ApplyAutoActivateTimer()
        {
            _autoActivateTimer?.Stop();
            if (_appSettings.AutoActivateMinutes <= 0) return;

            _autoActivateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(_appSettings.AutoActivateMinutes)
            };
            _autoActivateTimer.Tick += (_, _) =>
            {
                foreach (var wv in _webViews)
                {
                    if (wv.CoreWebView2 is null) continue;
                    if (!Uri.TryCreate(wv.CoreWebView2.Source, UriKind.Absolute, out var uri)) continue;
                    if (!uri.AbsolutePath.TrimEnd('/').Equals("/home", StringComparison.OrdinalIgnoreCase)) continue;
                    if (_webViewToPane.TryGetValue(wv, out var pane) &&
                        _paneToSetFocus.TryGetValue(pane, out var setFocus))
                    {
                        setFocus();
                        break;
                    }
                }
            };
            _autoActivateTimer.Start();
        }

        private void ApplySavedTheme()
        {
            ((FrameworkElement)Content).RequestedTheme = _appSettings.Theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark"  => ElementTheme.Dark,
                _       => ElementTheme.Default,
            };
            ApplyThemeToWebViews();
        }

        private async void AppSettingsBtn_Click(object _, RoutedEventArgs __)
        {
            var themeCombo = new ComboBox
            {
                ItemsSource   = new List<string> { "システム", "ライト", "ダーク" },
                SelectedIndex = _appSettings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 },
                MinWidth      = 140
            };

            var separateEnvToggle = new ToggleSwitch
            {
                IsOn              = false,
                IsEnabled         = false, // 廃止予定 (#17)
                OnContent         = "有効",
                OffContent        = "無効",
                Margin              = new Thickness(12, 0, 0, 0),
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var openFolderBtn = new Button { Content = "フォルダーを開く" };
            openFolderBtn.Click += async (_, _) =>
            {
                var folder = Path.GetDirectoryName(SettingsFilePath)!;
                Directory.CreateDirectory(folder);
                await Windows.System.Launcher.LaunchFolderPathAsync(folder);
            };

            var version = System.Reflection.Assembly.GetExecutingAssembly()
                              .GetName().Version?.ToString(3) ?? "不明";

            var edgeChannel = FindEdgeDevVersionFolder() is not null ? "Edge Dev" : "WebView2 ランタイム";
            string edgeVersion;
            try
            {
                edgeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString(
                    FindEdgeDevVersionFolder() ?? "");
            }
            catch
            {
                edgeVersion = _webViewEnv?.BrowserVersionString ?? "不明";
            }
            var versionInfoText = $"XTimelineViewer v{version}\r\n{edgeChannel} {edgeVersion}";

            // ヘルパー：左ラベル＋右コントロールの行を作る
            static Grid MakeRow(string label, FrameworkElement control, Thickness? margin = null)
            {
                var g = new Grid { Margin = margin ?? new Thickness(0, 6, 0, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var lbl = new TextBlock
                {
                    Text = label,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(lbl, 0);
                Grid.SetColumn(control, 1);
                g.Children.Add(lbl);
                g.Children.Add(control);
                return g;
            }

            var panel = new StackPanel { MinWidth = 400 };
            panel.Children.Add(MakeRow("テーマ", themeCombo, new Thickness(0)));
            panel.Children.Add(MakeRow("設定ファイルのエクスポート", openFolderBtn));
            panel.Children.Add(new NavigationViewItemSeparator { Margin = new Thickness(0, 12, 0, 8) });
            panel.Children.Add(new TextBlock
            {
                Text       = "試験機能",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            panel.Children.Add(MakeRow("投稿画面を別プロファイルで開く（廃止予定）", separateEnvToggle));

            var openPostToggle = new ToggleSwitch
            {
                IsOn                = _appSettings.OpenComposerInBrowser,
                OnContent           = "有効",
                OffContent          = "無効",
                Margin              = new Thickness(12, 0, 0, 0),
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            panel.Children.Add(MakeRow("新規投稿を外部ブラウザーで開く", openPostToggle));

            var openTweetToggle = new ToggleSwitch
            {
                IsOn                = _appSettings.OpenTweetInBrowser,
                OnContent           = "有効",
                OffContent          = "無効",
                Margin              = new Thickness(12, 0, 0, 0),
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            panel.Children.Add(MakeRow("ツイートを外部ブラウザーで開く", openTweetToggle));

            var autoActivateBox = new NumberBox
            {
                Value                   = _appSettings.AutoActivateMinutes,
                Minimum                 = 0,
                Maximum                 = 60,
                SmallChange             = 1,
                LargeChange             = 5,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                Width                   = 160,
            };
            panel.Children.Add(MakeRow("ホームタイムラインを定期的にアクティブ化（分、0 で無効）", autoActivateBox));
            panel.Children.Add(new NavigationViewItemSeparator { Margin = new Thickness(0, 12, 0, 8) });
            panel.Children.Add(new TextBlock
            {
                Text       = "バージョン情報",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            var monoFont   = new FontFamily("Cascadia Mono, Consolas, Courier New");
            var versionInfoBox = new StackPanel
            {
                Margin  = new Thickness(0, 6, 0, 0),
                Spacing = 2,
                Children =
                {
                    new TextBlock
                    {
                        Text                   = $"XTimelineViewer v{version}",
                        FontFamily             = monoFont,
                        FontSize               = 11,
                        IsTextSelectionEnabled = true,
                    },
                    new TextBlock
                    {
                        Text                   = $"{edgeChannel} {edgeVersion}",
                        FontFamily             = monoFont,
                        FontSize               = 11,
                        IsTextSelectionEnabled = true,
                    },
                }
            };

            var copyBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing     = 6,
                    Children    =
                    {
                        new FontIcon
                        {
                            Glyph      = "",
                            FontFamily = new FontFamily("Segoe Fluent Icons"),
                            FontSize   = 14
                        },
                        new TextBlock { Text = "コピー" }
                    }
                },
                Margin = new Thickness(0, 8, 8, 0)
            };
            copyBtn.Click += (_, _) =>
            {
                var dp = new DataPackage();
                dp.SetText(versionInfoText);
                Clipboard.SetContent(dp);
            };

            var issueBody = Uri.EscapeDataString(
                $"- アプリバージョン：v{version}\n" +
                $"- 内部の Edge バージョン：{edgeChannel} {edgeVersion}\n" +
                $"- 具体的な症状：\n");
            var issueUrl = $"https://github.com/daruyanagi/XTimelineViewer/issues/new?labels=bug&title=&body={issueBody}";

            var issueBtn = new HyperlinkButton
            {
                Content    = "Issue を報告",
                Margin     = new Thickness(0, 8, 0, 0),
                NavigateUri = new Uri(issueUrl),
            };

            var actionsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            actionsPanel.Children.Add(copyBtn);
            actionsPanel.Children.Add(issueBtn);

            panel.Children.Add(versionInfoBox);
            panel.Children.Add(actionsPanel);

            var dlg = new ContentDialog
            {
                Title             = "アプリ設定",
                Content           = panel,
                PrimaryButtonText = "保存",
                CloseButtonText   = "キャンセル",
                DefaultButton     = ContentDialogButton.Primary,
                XamlRoot          = Content.XamlRoot,
                RequestedTheme    = ((FrameworkElement)Content).ActualTheme
            };

            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            {
                _appSettings.Theme = themeCombo.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "Default" };
                _appSettings.OpenComposerInBrowser   = openPostToggle.IsOn;
                _appSettings.OpenTweetInBrowser  = openTweetToggle.IsOn;
                _appSettings.AutoActivateMinutes = (int)Math.Clamp(autoActivateBox.Value, 0, 60);
                SaveSettings();
                ApplySavedTheme();

                // 設定変更を即時反映する
                var flag = _appSettings.OpenTweetInBrowser ? "true" : "false";
                foreach (var wv in _webViews)
                    if (wv.CoreWebView2 is not null)
                        await wv.CoreWebView2.ExecuteScriptAsync(
                            $"window._xtvOpenTweetInBrowser = {flag};");
                ApplyAutoActivateTimer();
            }
        }

        // ── Theme ─────────────────────────────────────────────────────────────

        private async void PostBtn_Click(object _, RoutedEventArgs __)
        {
            if (_appSettings.OpenComposerInBrowser)
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://x.com/compose/post"));
            else
                await OpenPostDialogAsync();
        }

        private async Task OpenPostDialogAsync(WebView2? senderWebView = null)
        {
            var webView = new WebView2 { Width = 500, MinHeight = 520 };

            var dlg = new ContentDialog
            {
                Content          = webView,
                CloseButtonText  = "閉じる",
                XamlRoot         = Content.XamlRoot,
                RequestedTheme   = ((FrameworkElement)Content).ActualTheme
            };

            var env = _appSettings.SeparateComposeEnv
                ? await GetOrCreateComposeEnvAsync()
                : await GetOrCreateEnvAsync();
            await webView.EnsureCoreWebView2Async(env);

            // テーマを適用
            var root = (FrameworkElement)Content;
            var scheme = root.ActualTheme switch
            {
                ElementTheme.Light => CoreWebView2PreferredColorScheme.Light,
                ElementTheme.Dark  => CoreWebView2PreferredColorScheme.Dark,
                _                  => CoreWebView2PreferredColorScheme.Auto,
            };
            webView.CoreWebView2.Profile.PreferredColorScheme = scheme;

            bool composerReady = false;

            webView.CoreWebView2.NavigationCompleted += async (s, args) =>
            {
                if (!args.IsSuccess) return;
                composerReady = true;
                await webView.CoreWebView2.ExecuteScriptAsync("""
                    (function() {
                        var id = 'xtv-compose-style';
                        if (document.getElementById(id)) return;
                        var s = document.createElement('style');
                        s.id = id;
                        s.textContent =
                            '[data-testid="primaryColumn"],' +
                            '[data-testid="sidebarColumn"],' +
                            'header[role="banner"],' +
                            '[data-testid="modalBackdrop"]' +
                            '{display:none!important}';
                        document.head.appendChild(s);
                    })();
                    """);
            };

            webView.CoreWebView2.NavigationStarting += (s, args) =>
            {
                if (composerReady && !args.Uri.Contains("/compose/post"))
                    dlg.Hide();
            };

            webView.Source = new Uri("https://x.com/compose/post");

            // WebView2 の Win32 HWND は XAML Popup より常に前面に描画されるため、
            // ダイアログ表示中はタイムライン WebView2 を非表示にして Z-order 問題を回避する
            foreach (var wv in _webViews)
                wv.Visibility = Visibility.Collapsed;
            try
            {
                await dlg.ShowAsync();
            }
            finally
            {
                foreach (var wv in _webViews)
                    wv.Visibility = Visibility.Visible;

                // ダイアログを閉じた後、キーボードフォーカスを WebView2 に戻す
                var target = senderWebView ?? _webViews.FirstOrDefault();
                if (target is not null &&
                    _webViewToPane.TryGetValue(target, out var pane) &&
                    _paneToSetFocus.TryGetValue(pane, out var setFocus))
                {
                    setFocus();
                }
            }
        }

        // ── Keyboard shortcuts ────────────────────────────────────────────────

        private void OnWebViewMessageReceived(WebView2 senderWebView, string message)
        {
            if (message.StartsWith("openTweet:") &&
                Uri.TryCreate(message[10..], UriKind.Absolute, out var tweetUri))
            {
                _ = Windows.System.Launcher.LaunchUriAsync(tweetUri);
                return;
            }

            switch (message)
            {
                case "focusNext": FocusAdjacentTimeline(senderWebView, +1); break;
                case "focusPrev": FocusAdjacentTimeline(senderWebView, -1); break;
                case "newPost":
                    if (_appSettings.OpenComposerInBrowser)
                        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://x.com/compose/post"));
                    else
                        _ = OpenPostDialogAsync(senderWebView);
                    break;
            }
        }

        private void FocusAdjacentTimeline(WebView2 senderWebView, int direction)
        {
            if (!_webViewToPane.TryGetValue(senderWebView, out var senderPane)) return;
            int idx  = TimelinePanel.Children.IndexOf(senderPane);
            int next = idx + direction;
            if (next < 0 || next >= TimelinePanel.Children.Count) return;
            var targetPane = (Grid)TimelinePanel.Children[next];
            if (_paneToSetFocus.TryGetValue(targetPane, out var setFocus))
            {
                setFocus();
                targetPane.StartBringIntoView();
            }
        }

        private void ApplyThemeToWebViews()
        {
            var root   = (FrameworkElement)Content;
            var scheme = root.RequestedTheme switch
            {
                ElementTheme.Light => CoreWebView2PreferredColorScheme.Light,
                ElementTheme.Dark  => CoreWebView2PreferredColorScheme.Dark,
                _                  => CoreWebView2PreferredColorScheme.Auto,
            };
            foreach (var wv in _webViews)
                if (wv.CoreWebView2 is not null)
                    wv.CoreWebView2.Profile.PreferredColorScheme = scheme;
        }

        // ── Persistence ───────────────────────────────────────────────────────

        private async Task SaveTimelinesAsync()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SaveFilePath)!);
            var json = JsonSerializer.Serialize(_configs, JsonOptions);
            await File.WriteAllTextAsync(SaveFilePath, json);
        }

        private async Task RestoreTimelinesAsync()
        {
            try
            {
                var json    = await File.ReadAllTextAsync(SaveFilePath);
                var configs = JsonSerializer.Deserialize<List<TimelineConfig>>(json);
                if (configs is not null)
                    foreach (var cfg in configs)
                        AddTimeline(cfg);
            }
            catch { /* ファイルが存在しない場合などは無視 */ }
        }

        // ── Drag & Drop ───────────────────────────────────────────────────────

        private void MainArea_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation          = DataPackageOperation.Link;
                e.DragUIOverride.Caption     = "タイムラインを追加";
                e.DragUIOverride.IsGlyphVisible = true;
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.None;
            }
        }

        private async void MainArea_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var deferral = e.GetDeferral();
            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    if (item is StorageFile file &&
                        file.FileType.Equals(".url", StringComparison.OrdinalIgnoreCase))
                    {
                        var url = await ParseUrlShortcutAsync(file);
                        if (url is not null && IsXUrl(url))
                            AddTimeline(new TimelineConfig { Url = url });
                    }
                }
            }
            finally { deferral.Complete(); }
        }

        private static async Task<string?> ParseUrlShortcutAsync(StorageFile file)
        {
            try
            {
                var lines = await FileIO.ReadLinesAsync(file);
                foreach (var line in lines)
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                        return line[4..].Trim();
            }
            catch { }
            return null;
        }

        private static bool IsXUrl(string url) =>
            url.Contains("x.com",       StringComparison.OrdinalIgnoreCase) ||
            url.Contains("twitter.com", StringComparison.OrdinalIgnoreCase);

        // ── AddTimeline ───────────────────────────────────────────────────────

        private void AddTimeline(TimelineConfig cfg)
        {
            _configs.Add(cfg);
            _ = SaveTimelinesAsync();

            DropHintBorder.Visibility = Visibility.Collapsed;
            TimelineScroll.Visibility = Visibility.Visible;

            // Pane
            var pane = new Grid
            {
                Width             = cfg.Width,
                Margin            = new Thickness(4),
                VerticalAlignment = VerticalAlignment.Stretch,
                BorderThickness   = new Thickness(1),
                CornerRadius      = new CornerRadius(8)
            };
            pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Header
            var headerGrid = new Grid { Padding = new Thickness(8, 4, 4, 4) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(headerGrid, 0);

            // Theme
            void ApplyPaneTheme(ElementTheme theme)
            {
                bool dark    = theme == ElementTheme.Dark;
                bool focused = _focusedHeaderGrid == headerGrid;
                pane.Background       = new SolidColorBrush(dark
                    ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 255, 255, 255));
                pane.BorderBrush      = new SolidColorBrush(dark
                    ? Color.FromArgb(255, 70, 70, 70) : Color.FromArgb(255, 210, 210, 210));
                headerGrid.Background = new SolidColorBrush(focused
                    ? (dark ? Color.FromArgb(255, 29,  78, 137) : Color.FromArgb(255,   0, 120, 212))
                    : (dark ? Color.FromArgb(255, 55,  55,  60) : Color.FromArgb(255, 235, 235, 240)));
            }
            ApplyPaneTheme(((FrameworkElement)Content).ActualTheme);
            pane.ActualThemeChanged += (s, _) => ApplyPaneTheme(pane.ActualTheme);

            // URL label
            string displayText = cfg.Url;
            if (Uri.TryCreate(cfg.Url, UriKind.Absolute, out var uri))
                displayText = uri.Host + uri.PathAndQuery;

            var urlLabel = new TextBlock
            {
                Text              = displayText,
                FontSize          = 12,
                TextTrimming      = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity           = 0.8
            };
            Grid.SetColumn(urlLabel, 0);

            // WebView2
            var webView = new WebView2
            {
                VerticalAlignment   = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetRow(webView, 1);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation       = Orientation.Horizontal,
                Spacing           = 4,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(buttonPanel, 1);

            var settingsBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE713", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14 },
                Width = 28, Height = 26,
                Padding = new Thickness(0)
            };
            ToolTipService.SetToolTip(settingsBtn, "設定");

            var closeBtn = new Button
            {
                Content = "✕", Width = 28, Height = 26,
                Padding = new Thickness(0), FontSize = 11
            };
            ToolTipService.SetToolTip(closeBtn, "閉じる");

            buttonPanel.Children.Add(settingsBtn);
            buttonPanel.Children.Add(closeBtn);

            headerGrid.Children.Add(urlLabel);
            headerGrid.Children.Add(buttonPanel);

            pane.Children.Add(headerGrid);
            pane.Children.Add(webView);
            TimelinePanel.Children.Add(pane);
            _webViews.Add(webView);
            _webViewToPane[webView] = pane;

            // ── Focus ─────────────────────────────────────────────────────────

            Action refreshHeader = () => ApplyPaneTheme(pane.ActualTheme);
            _headerRefreshers.Add(refreshHeader);

            void SetFocus()
            {
                _focusedHeaderGrid = headerGrid;
                foreach (var r in _headerRefreshers) r();
                webView.Focus(FocusState.Programmatic);
            }
            _paneToSetFocus[pane] = SetFocus;

            headerGrid.Tapped        += (s, e) => SetFocus();
            headerGrid.DoubleTapped  += (s, e) =>
            {
                SetFocus();
                webView.Source = new Uri(cfg.Url);
            };
            webView.GotFocus   += (s, e) =>
            {
                _focusedHeaderGrid = headerGrid;
                foreach (var r in _headerRefreshers) r();
            };

            // ── Drag & Drop reorder ───────────────────────────────────────────

            headerGrid.CanDrag = true;
            headerGrid.DragStarting += (s, args) =>
            {
                _draggingPane = pane;
                args.Data.SetText("xtv-pane");
            };

            pane.AllowDrop = true;
            pane.DragOver  += (s, args) =>
            {
                if (_draggingPane is not null && _draggingPane != pane)
                {
                    args.AcceptedOperation = DataPackageOperation.Move;
                    args.Handled = true;
                }
            };
            pane.Drop += (s, args) =>
            {
                if (_draggingPane is null || _draggingPane == pane) return;
                args.Handled = true;

                var dragging = _draggingPane;

                int from = TimelinePanel.Children.IndexOf(dragging);
                int to   = TimelinePanel.Children.IndexOf(pane);
                if (from < 0 || to < 0) return;

                TimelinePanel.Children.RemoveAt(from);
                TimelinePanel.Children.Insert(to, dragging);

                var cfg2 = _configs[from];
                _configs.RemoveAt(from);
                _configs.Insert(to, cfg2);

                _ = SaveTimelinesAsync();
                dragging.Opacity = 1.0;
                _draggingPane = null;

                // 視覚ツリーへの再挿入後、WebView2 の Win32 HWND を再アンカーさせる
                dragging.Visibility = Visibility.Collapsed;
                dragging.UpdateLayout();
                dragging.Visibility = Visibility.Visible;
            };
            pane.DragLeave += (s, args) => pane.Opacity = 1.0;
            headerGrid.DragStarting += (s, args) => pane.Opacity = 0.5;

            // ── Settings dialog ───────────────────────────────────────────────

            settingsBtn.Click += async (s, _) =>
            {
                var widthBox = new NumberBox
                {
                    Value                   = cfg.Width,
                    Minimum                 = 100,
                    Maximum                 = 2000,
                    SmallChange             = 10,
                    LargeChange             = 50,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                    Width                   = 160
                };

                var hideHeaderToggle = new ToggleSwitch
                {
                    IsOn       = cfg.HideHeader,
                    OnContent  = "非表示",
                    OffContent = "表示"
                };

                var hideComposeToggle = new ToggleSwitch
                {
                    IsOn       = cfg.HideCompose,
                    OnContent  = "非表示",
                    OffContent = "表示"
                };

                var panel = new StackPanel { Spacing = 8 };
                panel.Children.Add(new TextBlock { Text = "タイムラインの幅（px）" });
                panel.Children.Add(widthBox);
                panel.Children.Add(new TextBlock
                {
                    Text   = "X の header 要素",
                    Margin = new Thickness(0, 8, 0, 0)
                });
                panel.Children.Add(hideHeaderToggle);
                panel.Children.Add(new TextBlock
                {
                    Text   = "投稿画面",
                    Margin = new Thickness(0, 8, 0, 0)
                });
                panel.Children.Add(hideComposeToggle);

                var dlg = new ContentDialog
                {
                    Title             = "設定",
                    Content           = panel,
                    PrimaryButtonText = "適用",
                    CloseButtonText   = "キャンセル",
                    DefaultButton     = ContentDialogButton.Primary,
                    XamlRoot          = Content.XamlRoot
                };

                if (await dlg.ShowAsync() == ContentDialogResult.Primary)
                {
                    cfg.Width  = Math.Clamp(widthBox.Value, 100, 2000);
                    pane.Width = cfg.Width;

                    cfg.HideHeader = hideHeaderToggle.IsOn;
                    if (webView.CoreWebView2 is not null)
                        await ApplyHideHeaderAsync(webView, cfg.HideHeader);

                    cfg.HideCompose = hideComposeToggle.IsOn;
                    if (webView.CoreWebView2 is not null)
                        await ApplyHideComposeAsync(webView, cfg.HideCompose);

                    await SaveTimelinesAsync();
                }
            };

            // ── Close ─────────────────────────────────────────────────────────

            closeBtn.Click += (s, e) =>
            {
                _configs.Remove(cfg);
                _webViews.Remove(webView);
                _webViewToPane.Remove(webView);
                _paneToSetFocus.Remove(pane);
                _headerRefreshers.Remove(refreshHeader);
                if (_focusedHeaderGrid == headerGrid)
                {
                    _focusedHeaderGrid = null;
                    foreach (var r in _headerRefreshers) r();
                }
                _ = SaveTimelinesAsync();

                TimelinePanel.Children.Remove(pane);
                if (TimelinePanel.Children.Count == 0)
                {
                    TimelineScroll.Visibility  = Visibility.Collapsed;
                    DropHintBorder.Visibility  = Visibility.Visible;
                }
            };

            _ = InitWebViewAsync(webView, cfg);
        }

        // ── WebView2 init ─────────────────────────────────────────────────────

        private static string BuildHideHeaderJs(bool hide) => $$"""
            (function(hide){
                var id='xtv-hide-header';
                var s=document.getElementById(id);
                if(hide){
                    if(!s){s=document.createElement('style');s.id=id;
                           s.textContent='header[role="banner"]{display:none!important}';
                           document.head.appendChild(s);}
                }else{
                    if(s)s.remove();
                }
            })({{(hide ? "true" : "false")}});
            """;

        private static async Task ApplyHideHeaderAsync(
            Microsoft.UI.Xaml.Controls.WebView2 webView, bool hide)
        {
            await webView.CoreWebView2.ExecuteScriptAsync(BuildHideHeaderJs(hide));
        }

        private static async Task ApplyAutoShowNewPostsAsync(WebView2 webView, string cfgUrl)
        {
            if (!Uri.TryCreate(cfgUrl, UriKind.Absolute, out var uri)) return;
            if (!uri.AbsolutePath.TrimEnd('/').Equals("/home", StringComparison.OrdinalIgnoreCase)) return;

            // 「（数字） 件のポストを表示」を含む button 要素を監視して自動でクリックするスクリプト
            await webView.CoreWebView2.ExecuteScriptAsync("""
                (function() {
                    var observer = new MutationObserver(function(mutations) {
                        mutations.forEach(function(mutation) {
                            mutation.addedNodes.forEach(function(node) {
                                if (node.nodeType === Node.ELEMENT_NODE) {
                                    var btn = node.matches('button') ? node : node.querySelector('button');
                                    if (btn && /件のポストを表示/.test(btn.textContent)) {
                                        btn.click();
                                    }
                                }
                            });
                        });
                    });
                    observer.observe(document.body, { childList: true, subtree: true });
                })();
                """);
        }

        private static bool EffectiveHideCompose(TimelineConfig cfg, string currentUrl) =>
            cfg.HideCompose && !currentUrl.Contains("compose/post", StringComparison.OrdinalIgnoreCase);

        private static string BuildHideComposeJs(bool hide) => $$"""
            (function(hide){
                var id='xtv-hide-compose';
                var s=document.getElementById(id);
                if(hide){
                    if(!s){s=document.createElement('style');s.id=id;
                           s.textContent='.r-1h8ys4a{display:none!important}';
                           document.head.appendChild(s);}
                }else{
                    if(s)s.remove();
                }
            })({{(hide ? "true" : "false")}});
            """;

        private static async Task ApplyHideComposeAsync(
            Microsoft.UI.Xaml.Controls.WebView2 webView, bool hide)
        {
            await webView.CoreWebView2.ExecuteScriptAsync(BuildHideComposeJs(hide));
        }

        private async Task LoadExtensionsAsync(WebView2 webView)
        {
            if (_extensionsLoaded) return;
            _extensionsLoaded = true;

            var extensionsDir = Path.Combine(AppContext.BaseDirectory, "extensions");
            if (!Directory.Exists(extensionsDir)) return;

            var errors = new System.Text.StringBuilder();

            foreach (var extDir in Directory.GetDirectories(extensionsDir))
            {
                try
                {
                    var ext = await webView.CoreWebView2.Profile.AddBrowserExtensionAsync(extDir);
                    AddExtensionButton(ext, extDir);
                }
                catch (Exception ex)
                {
                    errors.AppendLine($"・{Path.GetFileName(extDir)}");
                    errors.AppendLine($"  {ex}");
                }
            }

            if (errors.Length > 0)
            {
                var dlg = new ContentDialog
                {
                    Title           = "拡張機能の読み込みに失敗しました",
                    Content         = new ScrollViewer
                    {
                        MaxHeight = 300,
                        Content   = new TextBlock
                        {
                            Text       = errors.ToString().TrimEnd()
                            + "\n\n" + _webViewEnv!.BrowserVersionString,
                            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                            FontSize   = 12,
                            IsTextSelectionEnabled = true,
                            TextWrapping = TextWrapping.Wrap
                        }
                    },
                    CloseButtonText = "閉じる",
                    XamlRoot        = Content.XamlRoot
                };
                await dlg.ShowAsync();
            }
        }

        private void AddExtensionButton(CoreWebView2BrowserExtension ext, string extDir)
        {
            // マニフェストから名前、options_ui.page、アイコンを取得
            string name      = ext.Name;
            string? optPage  = null;
            string? iconPath = null;
            var manifestPath = Path.Combine(extDir, "manifest.json");
            if (File.Exists(manifestPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("options_ui", out var optUi) &&
                    optUi.TryGetProperty("page", out var page))
                    optPage = page.GetString();
                if (root.TryGetProperty("icons", out var icons))
                {
                    foreach (var size in new[] { "16", "32", "48", "128" })
                    {
                        if (icons.TryGetProperty(size, out var iconProp))
                        {
                            var iconFile = iconProp.GetString();
                            if (iconFile is not null)
                            {
                                var full = Path.Combine(extDir, iconFile);
                                if (File.Exists(full)) { iconPath = full; break; }
                            }
                        }
                    }
                }
            }
            if (optPage is null) return; // options_ui がない拡張は追加しない

            object btnContent = iconPath is not null
                ? new Image
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconPath)),
                    Width = 20, Height = 20
                }
                : (object)"🧩";

            var btn = new Button
            {
                Content = btnContent,
                Width   = 32,
                Height  = 32,
                Padding = new Thickness(0),
            };
            ToolTipService.SetToolTip(btn, $"{name} の設定");

            var optPageUrl = $"chrome-extension://{ext.Id}/{optPage}";
            btn.Click += async (_, _) =>
            {
                var optWebView = new WebView2 { Width = 480, MinHeight = 200 };
                var dlg = new ContentDialog
                {
                    Title           = $"{name} の設定",
                    Content         = optWebView,
                    CloseButtonText = "閉じる",
                    XamlRoot        = Content.XamlRoot
                };
                var env = await GetOrCreateEnvAsync();
                await optWebView.EnsureCoreWebView2Async(env);
                optWebView.Source = new Uri(optPageUrl);
                await dlg.ShowAsync();
            };

            // 設定ボタン（末尾）の左隣に挿入
            int insertIdx = Math.Max(0, RightToolbar.Children.Count - 1);
            RightToolbar.Children.Insert(insertIdx, btn);
        }

        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XTimelineViewer", "error.log");

        private static void LogError(string context, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                File.AppendAllText(LogFilePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}\n{ex}\n\n");
            }
            catch { /* ログ書き込み失敗は無視 */ }
        }

        private async Task InitWebViewAsync(WebView2 webView, TimelineConfig cfg)
        {
            try
            {
                var env = await GetOrCreateEnvAsync();
                await webView.EnsureCoreWebView2Async(env);
                await LoadExtensionsAsync(webView);
                ApplyThemeToWebViews();
            }
            catch (Exception ex)
            {
                LogError($"InitWebViewAsync (url={cfg.Url})", ex);

                // XamlRoot が準備できていない場合があるので、ループで待機する
                for (int i = 0; i < 20 && Content.XamlRoot is null; i++)
                    await Task.Delay(100);

                if (Content.XamlRoot is not null)
                {
                    var dlg = new ContentDialog
                    {
                        Title           = "WebView2 の初期化に失敗しました",
                        Content         = new ScrollViewer
                        {
                            MaxHeight = 300,
                            Content   = new TextBlock
                            {
                                Text = $"EdgeDevAppDir: {EdgeDevAppDir}\n\nログ: {LogFilePath}\n\n{ex}",
                                FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                                FontSize   = 12,
                                IsTextSelectionEnabled = true,
                                TextWrapping = TextWrapping.Wrap
                            }
                        },
                        CloseButtonText = "閉じる",
                        XamlRoot        = Content.XamlRoot
                    };
                    await dlg.ShowAsync();
                }
                return;
            }



            // キーボードショートカット：ブラウザ既定アクセラレータを無効化し JS で代替処理
            webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(KeyboardShortcutScript);
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(TweetInterceptScript);
            webView.CoreWebView2.WebMessageReceived += (s, e) =>
                OnWebViewMessageReceived(webView, e.TryGetWebMessageAsString());

            // 外部リンクをシステム既定ブラウザーで開く
            webView.CoreWebView2.NewWindowRequested += async (s, args) =>
            {
                args.Handled = true;
                await Windows.System.Launcher.LaunchUriAsync(new Uri(args.Uri));
            };

            webView.CoreWebView2.NavigationStarting += async (s, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var nav)) return;

                if (Uri.TryCreate(cfg.Url, UriKind.Absolute, out var origin) &&
                    !nav.Host.Equals(origin.Host, StringComparison.OrdinalIgnoreCase))
                {
                    args.Cancel = true;
                    await Windows.System.Launcher.LaunchUriAsync(nav);
                    return;
                }

                if (_appSettings.OpenTweetInBrowser &&
                    nav.AbsolutePath.Contains("/status/", StringComparison.OrdinalIgnoreCase))
                {
                    args.Cancel = true;
                    await Windows.System.Launcher.LaunchUriAsync(nav);
                }
            };

            webView.CoreWebView2.NavigationCompleted += async (s, args) =>
            {
                if (args.IsSuccess)
                {
                    await ApplyHideHeaderAsync(webView, cfg.HideHeader);
                    await ApplyHideComposeAsync(webView, EffectiveHideCompose(cfg, webView.CoreWebView2.Source));

                    // TweetInterceptScript のフラグを設定と同期する
                    var flag = _appSettings.OpenTweetInBrowser ? "true" : "false";
                    await webView.CoreWebView2.ExecuteScriptAsync(
                        $"window._xtvOpenTweetInBrowser = {flag};");

                    // x.com/home の場合だけ新着ポスト自動表示機能を適用する
                    if (Uri.TryCreate(webView.CoreWebView2.Source, UriKind.Absolute, out var current) &&
                        current.AbsolutePath.TrimEnd('/').Equals("/home", StringComparison.OrdinalIgnoreCase))
                    {
                        await ApplyAutoShowNewPostsAsync(webView, cfg.Url);
                    }
                }
            };

            webView.CoreWebView2.SourceChanged += async (s, args) =>
            {
                if (cfg.HideCompose)
                    await ApplyHideComposeAsync(webView, EffectiveHideCompose(cfg, webView.CoreWebView2.Source));
            };

            webView.Source = new Uri(cfg.Url);
        }
    }
}
