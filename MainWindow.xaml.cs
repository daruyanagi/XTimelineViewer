using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
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
        public bool   HideHeader { get; set; } = false;
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

        private void ThemeToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            var root = (FrameworkElement)Content;
            root.RequestedTheme = root.RequestedTheme switch
            {
                ElementTheme.Light => ElementTheme.Dark,
                ElementTheme.Dark  => ElementTheme.Default,
                _                  => ElementTheme.Light,
            };
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
                Content = "⚙", Width = 28, Height = 26,
                Padding = new Thickness(0), FontSize = 14
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

            // WebView2
            var webView = new WebView2
            {
                VerticalAlignment   = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetRow(webView, 1);

            pane.Children.Add(headerGrid);
            pane.Children.Add(webView);
            TimelinePanel.Children.Add(pane);
            _webViews.Add(webView);

            // ── Focus ─────────────────────────────────────────────────────────

            Action refreshHeader = () => ApplyPaneTheme(pane.ActualTheme);
            _headerRefreshers.Add(refreshHeader);

            void SetFocus()
            {
                _focusedHeaderGrid = headerGrid;
                foreach (var r in _headerRefreshers) r();
                webView.Focus(FocusState.Programmatic);
            }

            headerGrid.Tapped        += (s, e) => SetFocus();
            headerGrid.DoubleTapped  += async (s, e) =>
            {
                SetFocus();
                if (webView.CoreWebView2 is not null)
                    await webView.CoreWebView2.ExecuteScriptAsync("window.scrollTo(0, 0);");
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

                var panel = new StackPanel { Spacing = 8 };
                panel.Children.Add(new TextBlock { Text = "タイムラインの幅（px）" });
                panel.Children.Add(widthBox);
                panel.Children.Add(new TextBlock
                {
                    Text   = "X の header 要素",
                    Margin = new Thickness(0, 8, 0, 0)
                });
                panel.Children.Add(hideHeaderToggle);

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

                    await SaveTimelinesAsync();
                }
            };

            // ── Close ─────────────────────────────────────────────────────────

            closeBtn.Click += (s, e) =>
            {
                _configs.Remove(cfg);
                _webViews.Remove(webView);
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

        private async Task InitWebViewAsync(WebView2 webView, TimelineConfig cfg)
        {
            await webView.EnsureCoreWebView2Async();
            ApplyThemeToWebViews();

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
                    await ApplyHideHeaderAsync(webView, cfg.HideHeader);
            };

            webView.Source = new Uri(cfg.Url);
        }
    }
}
