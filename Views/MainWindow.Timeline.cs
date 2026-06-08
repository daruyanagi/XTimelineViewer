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

        // ── Quick add from menu (#120) ────────────────────────────────────────

        internal const string HomeTimelineUrl          = "https://x.com/home";
        internal const string NotificationsTimelineUrl = "https://x.com/notifications";
        internal const string BookmarksTimelineUrl     = "https://x.com/i/bookmarks";

        private void AddHomeTimelineItem_Click(object _, RoutedEventArgs __)
            => AddTimeline(new TimelineConfig { Url = HomeTimelineUrl });

        private void AddNotificationsTimelineItem_Click(object _, RoutedEventArgs __)
            => AddTimeline(new TimelineConfig { Url = NotificationsTimelineUrl });

        private void AddBookmarksTimelineItem_Click(object _, RoutedEventArgs __)
            => AddTimeline(new TimelineConfig { Url = BookmarksTimelineUrl });

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
                displayText = uri.Host + uri.PathAndQuery;

            var typeIcon = new FontIcon
            {
                Glyph             = GetTimelineGlyph(cfg.Url),
                FontFamily        = new FontFamily("Segoe Fluent Icons"),
                FontSize          = 14,
                Opacity           = 0.8,
                Margin            = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(typeIcon, 0);
            var hardReloadTooltip = new ToolTip();
            ToolTipService.SetToolTip(typeIcon, hardReloadTooltip);

            // Profile badge
            var profileBadge = CreateProfileBadge(cfg.ProfileId);
            Grid.SetColumn(profileBadge, 1);

            var urlLabel = new TextBlock
            {
                Text              = displayText,
                FontSize          = 12,
                TextTrimming      = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity           = 0.8
            };
            Grid.SetColumn(urlLabel, 2);

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
            Grid.SetColumn(buttonPanel, 3);

            var settingsBtn = new Button
            {
                Content = new FontIcon { Glyph = "", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14 },
                Width = 28, Height = 26,
                Padding = new Thickness(0)
            };
            ToolTipService.SetToolTip(settingsBtn, R.Get("Pane_Settings_Tooltip"));
            AutomationProperties.SetName(settingsBtn, R.Get("Pane_Settings_Tooltip"));
            AutomationProperties.SetName(webView, displayText);

            buttonPanel.Children.Add(settingsBtn);

            headerGrid.Children.Add(typeIcon);
            headerGrid.Children.Add(profileBadge);
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

                var listHeaderApplicable = IsListHeaderApplicable(cfg.Url);
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

                var panel = new StackPanel { Spacing = 8 };
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
                    _headerRefreshers.Remove(refreshHeader);
                    if (_focusedHeaderGrid == headerGrid)
                    {
                        _focusedHeaderGrid = null;
                        foreach (var r in _headerRefreshers) r();
                    }
                    await SaveTimelinesAsync();

                    TimelinePanel.Children.Remove(pane);
                    ViewModel.HasTimelines = TimelinePanel.Children.Count > 0;
                }
                else if (result == ContentDialogResult.Primary)
                {
                    var prevProfileId = cfg.ProfileId;
                    cfg.ProfileId = (profileBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "default";

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
                    }

                    StartHardReloadTimer(webView, cfg);
                    await SaveTimelinesAsync();
                }
            };

            _ = InitWebViewAsync(webView, cfg);
        }
    }
}
