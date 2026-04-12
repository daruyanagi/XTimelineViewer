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

    public sealed partial class MainWindow : Window
    {
        private static readonly string SaveFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XTimelineViewer", "timelines.json");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private readonly List<TimelineConfig> _configs = [];
        private Grid? _draggingPane;
        private Grid? _focusedHeaderGrid;
        private readonly List<Action> _headerRefreshers = [];
        private readonly List<WebView2> _webViews = [];
        private bool _extensionsLoaded = false;
        private CoreWebView2Environment? _webViewEnv;
        private readonly Dictionary<WebView2, Grid> _webViewToPane  = [];
        private readonly Dictionary<Grid, Action>   _paneToSetFocus = [];

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

        public MainWindow()
        {
            this.InitializeComponent();
            AppWindow.Resize(new SizeInt32(1400, 900));
            Title = $"XTimelineViewer — {SaveFilePath}";
            Closed += async (s, e) => await SaveTimelinesAsync();
            ((FrameworkElement)Content).ActualThemeChanged += (s, e) => { UpdateThemeToggleBtn(); ApplyThemeToWebViews(); };
            UpdateThemeToggleBtn();
            _ = RestoreTimelinesAsync();
        }

        // ── Theme ─────────────────────────────────────────────────────────────

        private void UpdateThemeToggleBtn()
        {
            var root = (FrameworkElement)Content;
            var (icon, tip) = root.RequestedTheme switch
            {
                ElementTheme.Light => ("☀", "ライト"),
                ElementTheme.Dark  => ("🌙", "ダーク"),
                _                  => ("⊙", "システム"),
            };
            ThemeToggleBtn.Content = icon;
            ToolTipService.SetToolTip(ThemeToggleBtn, tip);
        }

        private async void PostBtn_Click(object _, RoutedEventArgs __) => await OpenPostDialogAsync();

        private async Task OpenPostDialogAsync()
        {
            var webView = new WebView2 { Width = 500, MinHeight = 520 };

            var dlg = new ContentDialog
            {
                Content         = webView,
                CloseButtonText = "閉じる",
                XamlRoot        = Content.XamlRoot
            };

            var env = await GetOrCreateEnvAsync();
            await webView.EnsureCoreWebView2Async(env);

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
            await dlg.ShowAsync();
        }

        // ── Keyboard shortcuts ────────────────────────────────────────────────

        private void OnWebViewMessageReceived(WebView2 senderWebView, string message)
        {
            switch (message)
            {
                case "focusNext": FocusAdjacentTimeline(senderWebView, +1); break;
                case "focusPrev": FocusAdjacentTimeline(senderWebView, -1); break;
                case "newPost":   _ = OpenPostDialogAsync();                break;
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

        private void ThemeLight_Click(object _, RoutedEventArgs __)
        {
            ((FrameworkElement)Content).RequestedTheme = ElementTheme.Light;
            UpdateThemeToggleBtn();
            ApplyThemeToWebViews();
        }

        private void ThemeDark_Click(object _, RoutedEventArgs __)
        {
            ((FrameworkElement)Content).RequestedTheme = ElementTheme.Dark;
            UpdateThemeToggleBtn();
            ApplyThemeToWebViews();
        }

        private void ThemeSystem_Click(object _, RoutedEventArgs __)
        {
            ((FrameworkElement)Content).RequestedTheme = ElementTheme.Default;
            UpdateThemeToggleBtn();
            ApplyThemeToWebViews();
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

                int from = TimelinePanel.Children.IndexOf(_draggingPane);
                int to   = TimelinePanel.Children.IndexOf(pane);
                if (from < 0 || to < 0) return;

                TimelinePanel.Children.RemoveAt(from);
                TimelinePanel.Children.Insert(to, _draggingPane);

                var cfg2 = _configs[from];
                _configs.RemoveAt(from);
                _configs.Insert(to, cfg2);

                _ = SaveTimelinesAsync();
                _draggingPane = null;
            };
            pane.DragLeave += (s, args) => pane.Opacity = 1.0;
            headerGrid.DragStarting += (s, args) => pane.Opacity = 0.5;
            pane.Drop              += (s, args) => { if (_draggingPane is not null) _draggingPane.Opacity = 1.0; };

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

            // ThemeToggleBtn の左隣に挿入
            int themeIdx = RightToolbar.Children.IndexOf(ThemeToggleBtn);
            RightToolbar.Children.Insert(themeIdx, btn);
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
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var nav) &&
                    Uri.TryCreate(cfg.Url, UriKind.Absolute, out var origin) &&
                    !nav.Host.Equals(origin.Host, StringComparison.OrdinalIgnoreCase))
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
