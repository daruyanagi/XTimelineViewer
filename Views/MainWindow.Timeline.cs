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

namespace XTimelineViewer.Views
{
    public sealed partial class MainWindow : Window
    {
        // ── Persistence ───────────────────────────────────────────────────────

        // 保存の同時実行を防ぐ。追加・並べ替えなどから fire-and-forget で呼ばれるため、
        // I/O が重なって timelines.json が競合するのを避ける（#338）。
        private readonly SemaphoreSlim _saveTimelinesLock = new(1, 1);

        private async Task SaveTimelinesAsync()
        {
            // JSON 化は UI スレッド上で同期的に行い、_configs のスナップショットを取る。
            // await の後で _configs が変更されても、書き出す内容がぶれないようにするため。
            var json = JsonSerializer.Serialize(_configs, JsonOptions);

            await _saveTimelinesLock.WaitAsync();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SaveFilePath)!);

                // 一時ファイルに書いてから置換する原子的保存（#338）。
                // WriteAllTextAsync は truncate してから書くため、途中で落ちると
                // timelines.json が壊れる。RestoreTimelinesAsync は例外を握りつぶすので、
                // 壊れると全ペインが黙って消えてしまう。
                var tmp = SaveFilePath + ".tmp";
                await File.WriteAllTextAsync(tmp, json);
                File.Move(tmp, SaveFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                // fire-and-forget の呼び出し元では観測されないので、ここで必ず記録する。
                LogError("SaveTimelinesAsync", ex);
            }
            finally
            {
                _saveTimelinesLock.Release();
            }
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
                case "off":           glyph = ""; tipKey = "AutoLoad_Off";           opacity = 0.35; break; // Cancel
                case "idle":         glyph = ""; tipKey = "AutoLoad_Paused_Away";   opacity = 0.5;  break; // ホーム以外
                case "paused-elsewhere": glyph = ""; tipKey = "AutoLoad_Paused_Elsewhere"; opacity = 0.5;  break; // 他ペイン編集中
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
                    // ツールチップにアクティブ化ホットキーを表示（#237）
                    ToolTipService.SetToolTip(label, string.Format(R.Get("Tooltip_ActivateHotkey"), n));
                }
                else
                {
                    label.Text       = string.Empty;
                    label.Visibility = Visibility.Collapsed;
                    ToolTipService.SetToolTip(label, null);
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

        // ── ペインの並べ替え（ドラッグ / キーボード #344）─────────────────
        // TimelinePanel の子と _configs を同じ順序で入れ替え、番号バッジを振り直して保存する。
        // to には「取り除く前のインデックス」を渡す（ドラッグ先ペインの位置）。
        private void MovePaneTo(Grid pane, int to)
        {
            int from = TimelinePanel.Children.IndexOf(pane);
            if (from < 0 || to < 0 || from == to) return;

            TimelinePanel.Children.RemoveAt(from);
            TimelinePanel.Children.Insert(to, pane);

            var cfg = _configs[from];
            _configs.RemoveAt(from);
            _configs.Insert(to, cfg);

            RefreshTimelineNumbers();  // 並び替え後に番号を振り直す（#225）
            _ = SaveTimelinesAsync();

            // 視覚ツリーへの再挿入後、WebView2 の Win32 HWND を再アンカーさせる
            pane.Visibility = Visibility.Collapsed;
            pane.UpdateLayout();
            pane.Visibility = Visibility.Visible;
        }

        // 隣のペインと入れ替える（#344）。端では止まる（ラップしない）。
        // 再挿入でフォーカスが外れるので、連続して押せるよう戻しておく。
        private void MovePaneAdjacent(Grid pane, int direction)
        {
            int from = TimelinePanel.Children.IndexOf(pane);
            if (from < 0) return;
            int to = from + direction;
            if (to < 0 || to >= TimelinePanel.Children.Count) return;

            MovePaneTo(pane, to);

            if (_paneToSetFocus.TryGetValue(pane, out var setFocus)) setFocus();
            pane.StartBringIntoView();  // 視界外なら横スクロールして表示（#231）
        }

        // Ctrl+Shift+←/→（WebView2 非フォーカス時）。アクティブなペインを動かす。
        private void MovePaneFromActive(int direction)
        {
            if (_focusedHeaderGrid is null) return;
            if (!_headerGridToPane.TryGetValue(_focusedHeaderGrid, out var pane)) return;
            MovePaneAdjacent(pane, direction);
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
                // コントラストテーマ中は Light/Dark ではなく HighContrast 辞書を引く（#341）。
                // 自動解決に任せられない手引きの引き方なので、ここでも場合分けが要る。
                var themeKey   = IsHighContrast() ? "HighContrast"
                               : theme == ElementTheme.Light ? "Light" : "Default";
                var themeDict  = (ResourceDictionary)Application.Current.Resources.ThemeDictionaries[themeKey];
                bool hc = themeKey == "HighContrast";

                pane.Background = (Brush)themeDict["TimelinePaneBackgroundBrush"];

                // コントラストテーマではフォーカスを「塗り」ではなく「枠」で示す（#341）。
                // ヘッダーを Highlight 色で塗ると、中の文字色までこちらで揃えない限り
                // 地と衝突する。枠なら子要素の配色に一切干渉せずに済む。
                bool outlineFocus = hc && focused;
                pane.BorderBrush     = (Brush)themeDict[outlineFocus
                    ? "TimelineHeaderFocusedBackgroundBrush"
                    : "TimelinePaneBorderBrush"];
                pane.BorderThickness = new Thickness(outlineFocus ? 2 : 1);

                headerGrid.Background = (Brush)themeDict[focused && !hc
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
            _paneToConfig[pane] = cfg;  // ペイン一時拡大からの幅復元用（#287）
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
                autoLoadIcon.Visibility = nowHome ? Visibility.Visible : Visibility.Collapsed;
            };

            // ── Focus ─────────────────────────────────────────────────────────

            Action refreshHeader = () => ApplyPaneTheme(pane.ActualTheme);
            _headerRefreshers[pane] = refreshHeader;

            void SetFocus()
            {
                _focusedHeaderGrid = headerGrid;
                foreach (var r in _headerRefreshers.Values) r();
                webView.Focus(FocusState.Programmatic);
            }
            _paneToSetFocus[pane] = SetFocus;

            headerGrid.Tapped        += (s, e) => SetFocus();
            headerGrid.DoubleTapped  += (s, e) =>
            {
                SetFocus();
                webView.Source = new Uri(cfg.Url);
            };
            // ⚙ でプロファイルを切り替えると WebView2 を作り直すので、
            // 購読をここに集めて両方から呼ぶ（#361）。
            // クロージャではなく引数 wv を使うのが重要。変数 webView は
            // 切り替えで再代入されるため、ハンドラーが自分の購読先とずれる。
            void AttachWebViewHandlers(WebView2 wv)
            {
                wv.GotFocus += (s, e) =>
                {
                    _focusedHeaderGrid = headerGrid;
                    foreach (var r in _headerRefreshers.Values) r();
                };
                wv.PointerEntered += (s, e) => { _pointerOverWebViews.Add(wv);    EvaluateHardReloadPause(wv); };
                wv.PointerExited  += (s, e) =>
                {
                    _pointerOverWebViews.Remove(wv);
                    EvaluateHardReloadPause(wv);
                };

                _hardReloadUiUpdaters[wv] = () => UpdateHardReloadTooltip(wv, hardReloadTooltip);
                EnsureHardReloadUiTimer();
            }
            AttachWebViewHandlers(webView);

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

                MovePaneTo(dragging, TimelinePanel.Children.IndexOf(pane));

                dragging.Opacity = 1.0;
                _draggingPane = null;
            };
            pane.DragLeave += (s, args) => pane.Opacity = 1.0;
            headerGrid.DragStarting += (s, args) => pane.Opacity = 0.5;

            // ── Settings dialog ───────────────────────────────────────────────

            settingsBtn.Click += async (s, e2) =>
            {
                var widthBox = new NumberBox
                {
                    Header                  = R.Get("Timeline_Width"),
                    Value                   = cfg.Width,
                    Minimum                 = 100,
                    Maximum                 = 2000,
                    SmallChange             = 10,
                    LargeChange             = 50,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                    Width                   = 160,
                    HorizontalAlignment     = HorizontalAlignment.Left,
                };

                var hideSidebarToggle = new ToggleSwitch
                {
                    Header     = R.Get("Timeline_Sidebar"),
                    IsOn       = cfg.HideSidebar,
                    OnContent  = R.Get("Toggle_Hide"),
                    OffContent = R.Get("Toggle_Show")
                };

                var hideComposeToggle = new ToggleSwitch
                {
                    Header     = R.Get("Timeline_Compose"),
                    IsOn       = cfg.HideCompose,
                    OnContent  = R.Get("Toggle_Hide"),
                    OffContent = R.Get("Toggle_Show")
                };

                var listHeaderApplicable = UrlHelper.IsListHeaderApplicable(cfg.Url);
                var hideListHeaderToggle = new ToggleSwitch
                {
                    Header     = R.Get("Timeline_ListHeader"),
                    IsOn       = cfg.HideListHeader,
                    IsEnabled  = listHeaderApplicable,
                    OnContent  = R.Get("Toggle_Hide"),
                    OffContent = R.Get("Toggle_Show")
                };

                var hardReloadToggle = new ToggleSwitch
                {
                    Header     = R.Get("Timeline_ReloadInterval"),
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
                    HorizontalAlignment     = HorizontalAlignment.Left,
                    IsEnabled               = cfg.HardReloadEnabled,
                };
                hardReloadToggle.Toggled += (_, _) =>
                    hardReloadIntervalBox.IsEnabled = hardReloadToggle.IsOn;
                // トグルと同じラベルのグループなので、見た目は変えず名前だけ UIA に与える（#344）
                AutomationProperties.SetName(hardReloadIntervalBox, R.Get("Timeline_ReloadInterval"));

                var deleteBtn = new Button
                {
                    Content             = R.Get("Pane_Delete"),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin              = new Thickness(0, 16, 0, 0),
                    Foreground          = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                };

                var profileBox = new ComboBox
                {
                    Header              = R.Get("Timeline_Profile"),
                    MinWidth            = 200,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
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
                // ラベルは別立ての TextBlock ではなく各コントロールの Header に持たせる。
                // コード生成でも UI Automation 上の関連付けが成立する（#344）。
                panel.Children.Add(profileBox);
                panel.Children.Add(widthBox);
                panel.Children.Add(hideSidebarToggle);
                panel.Children.Add(hideComposeToggle);
                panel.Children.Add(hideListHeaderToggle);
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
                    if (_enlargedPane == pane) RestorePaneSize();  // 拡大中のペイン削除に備える（#287）
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
                    _paneToConfig.Remove(pane);
                    _headerRefreshers.Remove(pane);
                    if (_focusedHeaderGrid == headerGrid)
                    {
                        _focusedHeaderGrid = null;
                        foreach (var r in _headerRefreshers.Values) r();
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
                        AttachWebViewHandlers(webView);  // 再購読しないとフォーカス表示とホバー制御が死ぬ（#361）

                        var newBadge = CreateProfileBadge(cfg.ProfileId);
                        // 列 2 がプロフィールバッジ（列 1 は種別アイコン）。初期生成側と揃える (#337)
                        Grid.SetColumn(newBadge, 2);
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
