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

using XTimelineViewer.Models;
using XTimelineViewer.Services;

namespace XTimelineViewer.Views
{
    public sealed partial class MainWindow : Window
    {
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
                e.DragUIOverride.Caption     = R.Get("DragCaption");
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
                        if (url is not null && UrlHelper.IsXUrl(url))
                            AddTimeline(CreateDefaultConfig(url));
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
                return UrlHelper.ParseUrlShortcut(lines);
            }
            catch { }
            return null;
        }

        // ── Quick add from menu (#120) ────────────────────────────────────────

        internal const string HomeTimelineUrl          = "https://x.com/home";
        internal const string NotificationsTimelineUrl = "https://x.com/notifications";
        internal const string BookmarksTimelineUrl     = "https://x.com/i/bookmarks";

        // リスト一覧はアカウント依存の URL（https://x.com/&lt;handle&gt;/lists）。
        // 実際に追加する URL はクリック時にプロファイルのハンドルから組み立てる。
        internal static string BuildListsUrl(string handle) => $"https://x.com/{handle}/lists";

        // ハンドル依存 URL の組み立てに使うスクリーンネームを解決する。
        // Name はユーザーが編集できるため、検出済みの ScreenName を優先する。
        private static string ResolveProfileHandle(ProfileConfig profile)
            => profile.ScreenName is { Length: > 0 } sn ? sn : profile.Name;

        private void AddHomeTimelineItem_Click(object _, RoutedEventArgs __)
            => AddTimeline(CreateDefaultConfig(HomeTimelineUrl));

        private void AddNotificationsTimelineItem_Click(object _, RoutedEventArgs __)
            => AddTimeline(CreateDefaultConfig(NotificationsTimelineUrl));

        private void AddBookmarksTimelineItem_Click(object _, RoutedEventArgs __)
            => AddTimeline(CreateDefaultConfig(BookmarksTimelineUrl));

        private void AddListsTimelineItem_Click(object _, RoutedEventArgs __)
        {
            // URL を組み立てる名前付きプロファイルを決める（AddTimeline の既定割り当てと同じ）
            var profile = _profiles.FirstOrDefault(p => p.Id != "default");
            if (profile is null) return;

            // 初期 URL はキャッシュ済みハンドルの推測。実ハンドルはペイン読み込み時に
            // EnsureListsUrlAsync がアクティブアカウントからライブ解決する (#211)。
            var cfg = CreateDefaultConfig(BuildListsUrl(ResolveProfileHandle(profile)));
            cfg.ProfileId    = profile.Id;
            cfg.IsListsIndex = true;
            AddTimeline(cfg);
        }

        // ── CreateDefaultConfig ───────────────────────────────────────────────

        /// <summary>
        /// AppSettings の既定値を適用した新規 TimelineConfig を生成する。
        /// 復元時（RestoreTimelinesAsync）は保存済み値をそのまま使うため、このヘルパーを経由しない。
        /// </summary>
        private TimelineConfig CreateDefaultConfig(string url) => new()
        {
            Url            = url,
            HideSidebar    = _appSettings.DefaultHideSidebar,
            HideCompose    = _appSettings.DefaultHideCompose,
            HideListHeader = _appSettings.DefaultHideListHeader,
        };

        // ── ホーム自動更新インジケーター（#207）─────────────────────────────────
        // JS から postMessage('homeAutoLoad:STATUS') された状態をアイコン＋ツールチップへ反映する。
        // 状態を唯一の真実とし、設定値とアイコンがズレないようにする。
        private void UpdateAutoLoadIndicator(Grid pane, string status)
        {
            if (!_autoLoadIndicators.TryGetValue(pane, out var ind)) return;

            string glyph, tipKey;
            double opacity;
            switch (status)
            {
                case "running":       glyph = ""; tipKey = "AutoLoad_Running";       opacity = 0.8;  break; // Refresh
                case "paused-scroll": glyph = ""; tipKey = "AutoLoad_Paused_Scroll"; opacity = 0.5;  break; // Pause
                case "paused-search": glyph = ""; tipKey = "AutoLoad_Paused_Search"; opacity = 0.5;  break;
                case "paused-input":  glyph = ""; tipKey = "AutoLoad_Paused_Input";  opacity = 0.5;  break;
                case "off":           glyph = ""; tipKey = "AutoLoad_Off";           opacity = 0.35; break; // Cancel
                default:              glyph = ""; tipKey = "AutoLoad_Paused";         opacity = 0.5;  break; // idle 等
            }
            ind.Icon.Glyph   = glyph;
            ind.Icon.Opacity = opacity;
            ind.Tip.Content  = R.Get(tipKey);
        }

        // ── タイムライン番号バッジ / 番号フォーカス（#225）──────────────────────
        // 表示順（TimelinePanel の子の並び）に従って 1..9 を割り当てる。10 個目以降はバッジ非表示。
        private void RefreshTimelineNumbers()
        {
            int n = 1;
            foreach (var child in TimelinePanel.Children)
            {
                if (child is not Grid pane) continue;
                if (!_paneNumberLabels.TryGetValue(pane, out var label)) continue;
                if (n <= 9)
                {
                    label.Text       = $"{n}.";
                    label.Visibility = Visibility.Visible;
                }
                else
                {
                    label.Text       = string.Empty;
                    label.Visibility = Visibility.Collapsed;
                }
                n++;
            }
        }

        // Ctrl+数字 で、表示順 oneBased 番目のタイムラインをアクティブ化する。
        private void FocusTimelineByIndex(int oneBased)
        {
            var panes = TimelinePanel.Children.OfType<Grid>().ToList();
            int i = oneBased - 1;
            if (i < 0 || i >= panes.Count) return;
            if (_paneToSetFocus.TryGetValue(panes[i], out var setFocus))
            {
                setFocus();
                panes[i].StartBringIntoView();  // 視界外なら横スクロールして表示（#231）
            }
        }

        // Ctrl+←/→（WebView2 非フォーカス時）。現在アクティブなペインを基準に隣へ移動する。
        // 未フォーカス時は先頭（→）/末尾（←）を基準にフォールバック。端では止まる（ラップしない）。
        private void FocusAdjacentFromActive(int direction)
        {
            var panes = TimelinePanel.Children.OfType<Grid>().ToList();
            if (panes.Count == 0) return;

            int cur = -1;
            if (_focusedHeaderGrid is not null &&
                _headerGridToPane.TryGetValue(_focusedHeaderGrid, out var curPane))
                cur = panes.IndexOf(curPane);

            int next = cur < 0 ? (direction > 0 ? 0 : panes.Count - 1) : cur + direction;
            if (next < 0 || next >= panes.Count) return;
            if (_paneToSetFocus.TryGetValue(panes[next], out var setFocus))
            {
                setFocus();
                panes[next].StartBringIntoView();
            }
        }

        // ── AddTimeline ───────────────────────────────────────────────────────

        private void AddTimeline(TimelineConfig cfg)
        {
            // ProfileId が未指定または default の場合、最初の名前付きプロファイルを割り当てる
            if (cfg.ProfileId == "default")
            {
                var named = _profiles.FirstOrDefault(p => p.Id != "default");
                if (named is not null) cfg.ProfileId = named.Id;
            }

            _configs.Add(cfg);
            _ = SaveTimelinesAsync();

            ViewModel.HasTimelines = true;

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
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // number (#225)
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // typeIcon
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // profileBadge
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // urlLabel
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // buttons
            Grid.SetRow(headerGrid, 0);

            // Theme
            void ApplyPaneTheme(ElementTheme theme)
            {
                // Application.Current.Resources はアプリレベルのテーマを参照するため、
                // 要素単位で RequestedTheme を設定している場合に正しい辞書が返らない。
                // pane.ActualTheme（解決済み）を使い ThemeDictionaries を直接引く。
                bool focused   = _focusedHeaderGrid == headerGrid;
                var themeKey   = theme == ElementTheme.Light ? "Light" : "Default";
                var themeDict  = (ResourceDictionary)Application.Current.Resources.ThemeDictionaries[themeKey];
                pane.Background       = (Brush)themeDict["TimelinePaneBackgroundBrush"];
                pane.BorderBrush      = (Brush)themeDict["TimelinePaneBorderBrush"];
                headerGrid.Background = (Brush)themeDict[focused
                    ? "TimelineHeaderFocusedBackgroundBrush"
                    : "TimelineHeaderBackgroundBrush"];
            }
            ApplyPaneTheme(((FrameworkElement)Content).ActualTheme);
            pane.ActualThemeChanged += (s, _) => ApplyPaneTheme(pane.ActualTheme);

            // URL label
            string displayText = cfg.Url;
            if (Uri.TryCreate(cfg.Url, UriKind.Absolute, out var uri))
                // 検索クエリの %xx を人間が読めるようデコードする（既存の検索ロジックを再利用）
                displayText = SearchQueryHelper.DecodeSearchPath(uri.Host + uri.PathAndQuery);

            // 番号バッジ（#225）。左から 1, 2, … を割り当て、Ctrl+数字 のフォーカス切り替えと対応づける。
            // 番号は表示順に依存するため、追加・削除・並び替え後に RefreshTimelineNumbers で振り直す。
            var numberLabel = new TextBlock
            {
                FontSize          = 12,
                FontWeight        = Microsoft.UI.Text.FontWeights.SemiBold,
                Opacity           = 0.6,
                Margin            = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(numberLabel, 0);
            _paneNumberLabels[pane] = numberLabel;

            var typeIcon = new FontIcon
            {
                Glyph             = UrlHelper.GetTimelineGlyph(cfg.Url),
                FontFamily        = new FontFamily("Segoe Fluent Icons"),
                FontSize          = 14,
                Opacity           = 0.8,
                Margin            = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(typeIcon, 1);
            var hardReloadTooltip = new ToolTip();
            ToolTipService.SetToolTip(typeIcon, hardReloadTooltip);

            // Profile badge
            var profileBadge = CreateProfileBadge(cfg.ProfileId);
            Grid.SetColumn(profileBadge, 2);

            var urlLabel = new TextBlock
            {
                Text              = displayText,
                FontSize          = 12,
                TextTrimming      = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity           = 0.8
            };
            Grid.SetColumn(urlLabel, 3);

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
            Grid.SetColumn(buttonPanel, 4);

            var settingsBtn = new Button
            {
                Content = new FontIcon { Glyph = "", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14 },
                Width = 28, Height = 26,
                Padding = new Thickness(0)
            };
            ToolTipService.SetToolTip(settingsBtn, R.Get("Pane_Settings_Tooltip"));
            AutomationProperties.SetName(settingsBtn, R.Get("Pane_Settings_Tooltip"));
            AutomationProperties.SetName(webView, displayText);

            // ホーム自動更新インジケーター（#207）。ホームペインのみ表示し、設定ギアの左に置く。
            var autoLoadIcon = new FontIcon
            {
                Glyph             = "",
                FontFamily        = new FontFamily("Segoe Fluent Icons"),
                FontSize          = 14,
                Opacity           = 0.8,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 2, 0),
                Visibility        = IsHomeConfig(cfg) ? Visibility.Visible : Visibility.Collapsed
            };
            var autoLoadTip = new ToolTip { Content = R.Get("AutoLoad_Off") };
            ToolTipService.SetToolTip(autoLoadIcon, autoLoadTip);
            buttonPanel.Children.Add(autoLoadIcon);
            _autoLoadIndicators[pane] = (autoLoadIcon, autoLoadTip);
            UpdateAutoLoadIndicator(pane, _appSettings.HomeAutoLoadEnabled ? "running" : "off");

            buttonPanel.Children.Add(settingsBtn);

            headerGrid.Children.Add(numberLabel);
            headerGrid.Children.Add(typeIcon);
            headerGrid.Children.Add(profileBadge);
            headerGrid.Children.Add(urlLabel);
            headerGrid.Children.Add(buttonPanel);

            pane.Children.Add(headerGrid);
            pane.Children.Add(webView);
            TimelinePanel.Children.Add(pane);
            _webViews.Add(webView);
            _webViewToPane[webView] = pane;
            _headerGridToPane[headerGrid] = pane;  // アクティブ判定用（#227）
            RefreshTimelineNumbers();  // 番号バッジを振り直す（#225）

            // cfg.Url の変更をヘッダーへ反映する更新子（ベース URL 変更・リスト URL ライブ解決で再利用）
            _paneUrlUpdaters[cfg] = () =>
            {
                urlLabel.Text = Uri.TryCreate(cfg.Url, UriKind.Absolute, out var u)
                    ? SearchQueryHelper.DecodeSearchPath(u.Host + u.PathAndQuery)
                    : cfg.Url;
                typeIcon.Glyph = UrlHelper.GetTimelineGlyph(cfg.Url);
                AutomationProperties.SetName(webView, urlLabel.Text);
                bool nowHome = Uri.TryCreate(cfg.Url, UriKind.Absolute, out var hu)
                            && hu.AbsolutePath.StartsWith("/home", StringComparison.OrdinalIgnoreCase);
                if (nowHome) _homeHeaderGrids.Add(headerGrid);
                else         _homeHeaderGrids.Remove(headerGrid);
                autoLoadIcon.Visibility = nowHome ? Visibility.Visible : Visibility.Collapsed;
            };

            // ── Focus ─────────────────────────────────────────────────────────

            Action refreshHeader = () => ApplyPaneTheme(pane.ActualTheme);
            _headerRefreshers.Add(refreshHeader);

            void SetFocus()
            {
                if (_focusedHeaderGrid is not null && _homeHeaderGrids.Contains(_focusedHeaderGrid) && !_homeHeaderGrids.Contains(headerGrid))
                    RestartAutoActivateTimer();  // ホームから別タイムラインへ
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
                if (_focusedHeaderGrid is not null && _homeHeaderGrids.Contains(_focusedHeaderGrid) && !_homeHeaderGrids.Contains(headerGrid))
                    RestartAutoActivateTimer();  // ホームから別タイムラインへ
                _focusedHeaderGrid = headerGrid;
                foreach (var r in _headerRefreshers) r();
            };
            webView.PointerEntered += (s, e) => { _pointerOverWebViews.Add(webView);    EvaluateHardReloadPause(webView); };
            webView.PointerExited  += (s, e) =>
            {
                _pointerOverWebViews.Remove(webView);
                EvaluateHardReloadPause(webView);
                if (_pointerOverWebViews.Count == 0) RestartAutoActivateTimer();
            };

            bool isHomeTimeline = Uri.TryCreate(cfg.Url, UriKind.Absolute, out var cfgUri)
                               && cfgUri.AbsolutePath.StartsWith("/home", StringComparison.OrdinalIgnoreCase);
            if (isHomeTimeline) _homeHeaderGrids.Add(headerGrid);

            _hardReloadUiUpdaters[webView] = () => UpdateHardReloadTooltip(webView, hardReloadTooltip);
            EnsureHardReloadUiTimer();

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

                RefreshTimelineNumbers();  // 並び替え後に番号を振り直す（#225）
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

            settingsBtn.Click += async (s, e2) =>
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

                var hideSidebarToggle = new ToggleSwitch
                {
                    IsOn       = cfg.HideSidebar,
                    OnContent  = R.Get("Toggle_Hide"),
                    OffContent = R.Get("Toggle_Show")
                };

                var hideComposeToggle = new ToggleSwitch
                {
                    IsOn       = cfg.HideCompose,
                    OnContent  = R.Get("Toggle_Hide"),
                    OffContent = R.Get("Toggle_Show")
                };

                var listHeaderApplicable = UrlHelper.IsListHeaderApplicable(cfg.Url);
                var hideListHeaderToggle = new ToggleSwitch
                {
                    IsOn       = cfg.HideListHeader,
                    IsEnabled  = listHeaderApplicable,
                    OnContent  = R.Get("Toggle_Hide"),
                    OffContent = R.Get("Toggle_Show")
                };
                var hideListHeaderLabel = new TextBlock
                {
                    Text    = R.Get("Timeline_ListHeader"),
                    Margin  = new Thickness(0, 8, 0, 0),
                    Opacity = listHeaderApplicable ? 1.0 : 0.4
                };

                var hardReloadToggle = new ToggleSwitch
                {
                    IsOn       = cfg.HardReloadEnabled,
                    OnContent  = R.Get("Toggle_On"),
                    OffContent = R.Get("Toggle_Off"),
                };
                var hardReloadIntervalBox = new NumberBox
                {
                    Value                   = cfg.HardReloadInterval,
                    Minimum                 = 1,
                    Maximum                 = 60,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                    Width                   = 160,
                    IsEnabled               = cfg.HardReloadEnabled,
                };
                hardReloadToggle.Toggled += (_, _) =>
                    hardReloadIntervalBox.IsEnabled = hardReloadToggle.IsOn;
                var reloadLabel = new TextBlock
                {
                    Text   = R.Get("Timeline_ReloadInterval"),
                    Margin = new Thickness(0, 8, 0, 0),
                };

                var deleteBtn = new Button
                {
                    Content             = R.Get("Pane_Delete"),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin              = new Thickness(0, 16, 0, 0),
                    Foreground          = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                };

                var profileBox = new ComboBox { MinWidth = 200 };
                foreach (var p in _profiles.Where(p => p.Id != "default"))
                    profileBox.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
                profileBox.SelectedItem = profileBox.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(i => (string)i.Tag == cfg.ProfileId)
                    ?? profileBox.Items.OfType<ComboBoxItem>().FirstOrDefault();

                // ── ベース URL (#189) ──
                // WebView2 の現在の表示 URL がベース URL と異なる（別ページを閲覧中）かつ
                // X の URL のときだけ「現在のページをベース URL にする」を有効化する。
                var currentSource = webView.CoreWebView2?.Source ?? cfg.Url;
                string? stagedBaseUrl = null;  // ［適用］で確定する新しいベース URL

                var baseUrlText = new TextBlock
                {
                    Text                   = SearchQueryHelper.DecodeSearchPath(cfg.Url),
                    FontSize               = 12,
                    Opacity                = 0.8,
                    TextWrapping           = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                };

                var setBaseUrlBtn = new Button
                {
                    Content             = R.Get("Timeline_SetBaseUrl"),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    IsEnabled           = UrlHelper.IsXUrl(currentSource)
                                       && !UrlHelper.IsOnBaseUrl(currentSource, cfg.Url),
                };
                setBaseUrlBtn.Click += (_, _) =>
                {
                    stagedBaseUrl        = currentSource;
                    baseUrlText.Text     = SearchQueryHelper.DecodeSearchPath(stagedBaseUrl);
                    setBaseUrlBtn.IsEnabled = false;
                };

                var panel = new StackPanel { Spacing = 8 };
                panel.Children.Add(new TextBlock { Text = R.Get("Timeline_BaseUrl") });
                panel.Children.Add(baseUrlText);
                panel.Children.Add(setBaseUrlBtn);
                panel.Children.Add(new NavigationViewItemSeparator { Margin = new Thickness(0, 8, 0, 0) });
                panel.Children.Add(new TextBlock { Text = R.Get("Timeline_Profile") });
                panel.Children.Add(profileBox);
                panel.Children.Add(new TextBlock { Text = R.Get("Timeline_Width"), Margin = new Thickness(0, 8, 0, 0) });
                panel.Children.Add(widthBox);
                panel.Children.Add(new TextBlock
                {
                    Text   = R.Get("Timeline_Sidebar"),
                    Margin = new Thickness(0, 8, 0, 0)
                });
                panel.Children.Add(hideSidebarToggle);
                panel.Children.Add(new TextBlock
                {
                    Text   = R.Get("Timeline_Compose"),
                    Margin = new Thickness(0, 8, 0, 0)
                });
                panel.Children.Add(hideComposeToggle);
                panel.Children.Add(hideListHeaderLabel);
                panel.Children.Add(hideListHeaderToggle);
                panel.Children.Add(reloadLabel);
                panel.Children.Add(hardReloadToggle);
                panel.Children.Add(hardReloadIntervalBox);
                panel.Children.Add(new NavigationViewItemSeparator { Margin = new Thickness(0, 8, 0, 0) });
                panel.Children.Add(deleteBtn);

                var dlg = new ContentDialog
                {
                    Title             = R.Get("Timeline_Settings_Title"),
                    Content           = new ScrollViewer
                    {
                        Content = panel,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    },
                    PrimaryButtonText = R.Get("Button_Apply"),
                    CloseButtonText   = R.Get("Button_Cancel"),
                    DefaultButton     = ContentDialogButton.Primary,
                    XamlRoot          = Content.XamlRoot
                };

                bool shouldDelete = false;
                deleteBtn.Click += (_, _) => { shouldDelete = true; dlg.Hide(); };

                var result = await ShowDialogAsync(dlg);

                if (shouldDelete)
                {
                    CleanupWebView(webView);
                    if (_hardReloadUiUpdaters.Count == 0)
                    {
                        _hardReloadUiTimer?.Stop();
                        _hardReloadUiTimer = null;
                    }
                    _configs.Remove(cfg);
                    _paneToSetFocus.Remove(pane);
                    _paneUrlUpdaters.Remove(cfg);
                    _autoLoadIndicators.Remove(pane);
                    _paneNumberLabels.Remove(pane);
                    _headerGridToPane.Remove(headerGrid);
                    _headerRefreshers.Remove(refreshHeader);
                    if (_focusedHeaderGrid == headerGrid)
                    {
                        _focusedHeaderGrid = null;
                        foreach (var r in _headerRefreshers) r();
                    }
                    await SaveTimelinesAsync();

                    TimelinePanel.Children.Remove(pane);
                    RefreshTimelineNumbers();  // 削除後に番号を振り直す（#225）
                    ViewModel.HasTimelines = TimelinePanel.Children.Count > 0;
                }
                else if (result == ContentDialogResult.Primary)
                {
                    // ベース URL の変更を反映 (#189)。プロファイル再生成より前に cfg.Url を
                    // 更新しておき、再生成時は新しいベース URL へ遷移させる。
                    bool baseUrlChanged = stagedBaseUrl is not null && stagedBaseUrl != cfg.Url;
                    if (baseUrlChanged)
                    {
                        cfg.Url = stagedBaseUrl!;
                        // 具体ページを明示的に固定したので、リスト一覧の自動追従は解除する (#211)
                        cfg.IsListsIndex = false;
                        _paneUrlUpdaters[cfg]();
                    }

                    var prevProfileId = cfg.ProfileId;
                    cfg.ProfileId = (profileBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "default";

                    // リスト一覧（IsListsIndex）はプロファイル切り替え後、再生成したペインの
                    // 読み込み時に EnsureListsUrlAsync がアクティブアカウントのハンドルで
                    // ライブ解決するため、ここでは URL を組み立てない (#211)

                    cfg.Width  = Math.Clamp(widthBox.Value, 100, 2000);
                    pane.Width = cfg.Width;

                    cfg.HideSidebar = hideSidebarToggle.IsOn;
                    cfg.HideCompose = hideComposeToggle.IsOn;
                    cfg.HideListHeader = hideListHeaderToggle.IsOn;
                    cfg.HardReloadEnabled  = hardReloadToggle.IsOn;
                    cfg.HardReloadInterval = (int)Math.Clamp(hardReloadIntervalBox.Value, 1, 60);

                    if (prevProfileId != cfg.ProfileId)
                    {
                        CleanupWebView(webView);
                        pane.Children.Remove(webView);

                        webView = new WebView2
                        {
                            VerticalAlignment   = VerticalAlignment.Stretch,
                            HorizontalAlignment = HorizontalAlignment.Stretch
                        };
                        Grid.SetRow(webView, 1);
                        pane.Children.Add(webView);
                        _webViews.Add(webView);
                        _webViewToPane[webView] = pane;
                        AutomationProperties.SetName(webView, urlLabel.Text);

                        var newBadge = CreateProfileBadge(cfg.ProfileId);
                        Grid.SetColumn(newBadge, 1);
                        headerGrid.Children.Remove(profileBadge);
                        headerGrid.Children.Add(newBadge);
                        profileBadge = newBadge;

                        Debug.WriteLine($"[Profile] WebView2 recreated for profile switch: {prevProfileId} -> {cfg.ProfileId}");
                        _ = InitWebViewAsync(webView, cfg);
                    }
                    else
                    {
                        if (webView.CoreWebView2 is not null)
                        {
                            await ApplyHideSidebarAsync(webView, cfg.HideSidebar);
                            await ApplyHideComposeAsync(webView, cfg.HideCompose);
                            await ApplyHideListHeaderAsync(webView, cfg.HideListHeader);
                        }

                        // ベース URL を現在のページに合わせたので乖離状態を解消し、
                        // 定期ハードリロードの一時停止を再評価する
                        if (baseUrlChanged)
                        {
                            _urlDivergedWebViews.Remove(webView);
                            EvaluateHardReloadPause(webView);
                        }
                    }

                    StartHardReloadTimer(webView, cfg);
                    await SaveTimelinesAsync();
                }
            };

            _ = InitWebViewAsync(webView, cfg);
        }
    }
}
