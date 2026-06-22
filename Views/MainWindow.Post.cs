using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Windows.UI;

using XTimelineViewer.Models;

namespace XTimelineViewer.Views
{
    public sealed partial class MainWindow : Window
    {
        private async void PostBtn_Click(object _, RoutedEventArgs __)
        {
            if (_appSettings.OpenComposerInBrowser)
                _ = LaunchUriByEdgeProfileAsync(new Uri("https://x.com/compose/post"));
            else
                await OpenPostDialogAsync();
        }

        // ── 投稿ウィンドウのプリロード（試験機能 #244 案A）─────────────────────────
        // 最後に使ったプロファイルの compose 画面を非表示ホストで実サイズまで完成させておき、
        // 投稿ボタン押下時はその「生成済みインスタンス」をダイアログへ移し替えて即表示する。
        // 閉じたらホストへ戻し、compose/post へ再ナビゲートして下書きをリセットし次回に備える。
        private WebView2? _composeWarmWebView;
        private string?   _composeWarmProfileId;
        private ContentDialog? _activeComposeDialog;             // 現在開いている投稿ダイアログ（自動クローズ用）
        private readonly HashSet<WebView2> _composeReadyViews = []; // compose/post ロード完了済みのビュー

        internal async Task WarmUpComposeAsync()
        {
            if (!_appSettings.ComposePreloadEnabled) { DisposeComposeWarm(); return; }

            var profileId = ResolveComposeProfileId();
            // 同じプロファイルで準備済みなら何もしない
            if (_composeWarmWebView is not null && _composeWarmProfileId == profileId) return;
            DisposeComposeWarm();

            try
            {
                var wv = CreateHiddenComposeHost();
                ((Grid)Content).Children.Add(wv);
                await AttachComposeBehavior(wv, profileId);  // env 生成 + ハンドラ + compose/post ナビゲート
                _composeWarmWebView   = wv;
                _composeWarmProfileId = profileId;
            }
            catch (Exception ex)
            {
                LogError("WarmUpComposeAsync", ex);
            }
        }

        // 非表示ホスト用に compose 実サイズで生成（表示時の再レイアウトを避けるため 1x1 にはしない）
        private static WebView2 CreateHiddenComposeHost()
        {
            var wv = new WebView2
            {
                Width  = 500,
                MinHeight = 520,
                Opacity = 0,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment   = VerticalAlignment.Top,
            };
            Canvas.SetZIndex(wv, -1);
            Grid.SetRow(wv, 1);  // コンテンツ行（Star）に置き、Auto 高のツールバー行を押し広げないようにする
            return wv;
        }

        private void DisposeComposeWarm()
        {
            if (_composeWarmWebView is null) return;
            try
            {
                _composeReadyViews.Remove(_composeWarmWebView);
                ((Grid)Content).Children.Remove(_composeWarmWebView);
                _composeWarmWebView.Close();
            }
            catch { /* 破棄失敗は無視 */ }
            _composeWarmWebView   = null;
            _composeWarmProfileId = null;
        }

        // 借りていた warm WebView2 を非表示ホストへ戻し、下書きをリセットして次回に備える
        private void ReturnWarmToHost(WebView2 wv, StackPanel rootPanel, string profileId)
        {
            try { rootPanel.Children.Remove(wv); } catch { }
            wv.Opacity             = 0;
            wv.IsHitTestVisible    = false;
            wv.HorizontalAlignment = HorizontalAlignment.Left;
            wv.VerticalAlignment   = VerticalAlignment.Top;
            Canvas.SetZIndex(wv, -1);
            Grid.SetRow(wv, 1);
            ((Grid)Content).Children.Add(wv);
            _composeReadyViews.Remove(wv);
            try { wv.Source = new Uri("https://x.com/compose/post"); } catch { }  // 下書きリセット
            _composeWarmWebView   = wv;
            _composeWarmProfileId = profileId;
        }

        private async Task OpenPostDialogAsync(WebView2? senderWebView = null)
        {
            // ── プロファイルセレクターの構築 ──
            var selectedProfileId = ResolveComposeProfileId();

            var profileCombo = BuildProfileComboBox(selectedProfileId);
            var rootPanel = new StackPanel { Spacing = 8 };
            rootPanel.Children.Add(profileCombo);

            // プリロード済み（warm）が同じプロファイルで使えるなら、移し替えて即表示（#244 案A）
            WebView2 webView;
            bool currentIsWarm;
            if (_appSettings.ComposePreloadEnabled &&
                _composeWarmWebView is not null &&
                _composeWarmProfileId == selectedProfileId)
            {
                webView = _composeWarmWebView;
                _composeWarmWebView = null;        // 借用中（閉じる時にホストへ戻す）
                currentIsWarm = true;
                ((Grid)Content).Children.Remove(webView);
                webView.Opacity          = 1;
                webView.IsHitTestVisible = true;
                Canvas.SetZIndex(webView, 0);
                rootPanel.Children.Add(webView);
            }
            else
            {
                webView = new WebView2 { Width = 500, MinHeight = 520 };
                currentIsWarm = false;
                rootPanel.Children.Add(webView);
            }

            var dlg = new ContentDialog
            {
                Content         = rootPanel,
                CloseButtonText = R.Get("Button_Close"),
                XamlRoot        = Content.XamlRoot,
            };
            _activeComposeDialog = dlg;

            if (!currentIsWarm)
                await AttachComposeBehavior(webView, selectedProfileId);

            // ── プロファイル切り替え（切替後は常にオンデマンド生成）──
            profileCombo.SelectionChanged += async (s, args) =>
            {
                if (profileCombo.SelectedItem is not ComboBoxItem item) return;
                var newProfileId = item.Tag as string ?? "default";
                if (newProfileId == selectedProfileId) return;
                var prevProfileId = selectedProfileId;
                selectedProfileId = newProfileId;

                // 現在の WebView2 を退避：warm は破棄せずホストへ戻す／オンデマンドは破棄
                if (currentIsWarm) ReturnWarmToHost(webView, rootPanel, prevProfileId);
                else { rootPanel.Children.Remove(webView); try { webView.Close(); } catch { } }

                webView = new WebView2 { Width = 500, MinHeight = 520 };
                currentIsWarm = false;
                rootPanel.Children.Add(webView);
                await AttachComposeBehavior(webView, selectedProfileId);
            };

            // WebView2 の Win32 HWND は XAML Popup より常に前面に描画されるため、
            // ダイアログ表示中はタイムライン WebView2 を非表示にして Z-order 問題を回避する
            foreach (var wv in _webViews)
                wv.Visibility = Visibility.Collapsed;
            try
            {
                await ShowDialogAsync(dlg);
            }
            finally
            {
                _activeComposeDialog = null;

                // 後始末：warm はホストへ戻して再利用、オンデマンドは破棄
                if (currentIsWarm) ReturnWarmToHost(webView, rootPanel, selectedProfileId);
                else { try { rootPanel.Children.Remove(webView); webView.Close(); } catch { } }

                foreach (var wv in _webViews)
                    wv.Visibility = Visibility.Visible;

                // 選択プロファイルを保存
                if (_appSettings.LastUsedProfileId != selectedProfileId)
                {
                    _appSettings.LastUsedProfileId = selectedProfileId;
                    SaveSettings();
                }

                // ダイアログを閉じた後、キーボードフォーカスを WebView2 に戻す
                var target = senderWebView ?? _webViews.FirstOrDefault();
                if (target is not null &&
                    _webViewToPane.TryGetValue(target, out var pane) &&
                    _paneToSetFocus.TryGetValue(pane, out var setFocus))
                {
                    setFocus();
                }

                // 次回に備えて再プリロード（設定 ON のとき。同プロファイルなら no-op）
                _ = WarmUpComposeAsync();
            }
        }

        /// <summary>投稿に使用するプロファイル ID を決定する。</summary>
        private string ResolveComposeProfileId()
        {
            // 前回使用したプロファイルが存在し、default でなければそれを使う
            if (_appSettings.LastUsedProfileId is { } last &&
                last != "default" &&
                _profiles.Any(p => p.Id == last))
                return last;

            // 名前付きプロファイルがあれば最初のものを使う
            var named = _profiles.FirstOrDefault(p => p.Id != "default");
            if (named is not null) return named.Id;

            return "default";
        }

        /// <summary>プロファイル選択 ComboBox を構築する。</summary>
        private ComboBox BuildProfileComboBox(string selectedProfileId)
        {
            var combo = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Header = R.Get("Compose_Profile"),
            };

            int selectedIndex = 0;
            int comboIdx = 0;
            for (int i = 0; i < _profiles.Count; i++)
            {
                var profile = _profiles[i];
                if (profile.Id == "default") continue;
                var color = GetProfileColor(profile, profile.Id);
                var item = new ComboBoxItem
                {
                    Tag     = profile.Id,
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing     = 8,
                        Children =
                        {
                            new Border
                            {
                                Width        = 12,
                                Height       = 12,
                                CornerRadius = new CornerRadius(2),
                                Background   = new SolidColorBrush(color),
                                VerticalAlignment = VerticalAlignment.Center,
                            },
                            new TextBlock
                            {
                                Text              = profile.Name,
                                VerticalAlignment = VerticalAlignment.Center,
                            },
                        },
                    },
                };
                combo.Items.Add(item);
                if (profile.Id == selectedProfileId) selectedIndex = comboIdx;
                comboIdx++;
            }

            combo.SelectedIndex = selectedIndex;
            return combo;
        }

        /// <summary>
        /// 投稿用 WebView2 を初期化してハンドラを取り付け、compose/post へナビゲートする。
        /// 自動クローズは現在開いている投稿ダイアログ（<see cref="_activeComposeDialog"/>）に対して行う。
        /// プリロード（warm）でも投稿ダイアログでも同じ振る舞いを共有する（#244 案A）。
        /// </summary>
        private async Task AttachComposeBehavior(WebView2 webView, string profileId)
        {
            var env = await GetOrCreateProfileEnvAsync(profileId);
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

            _composeReadyViews.Remove(webView);

            webView.CoreWebView2.NavigationCompleted += async (s, args) =>
            {
                if (!args.IsSuccess) return;
                _composeReadyViews.Add(webView);
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
                if (_composeReadyViews.Contains(webView) && !args.Uri.Contains("/compose/post"))
                    _activeComposeDialog?.Hide();
            };

            // SPA 遷移では NavigationStarting が発火しないため、
            // 投稿 API のレスポンスを監視して自動で閉じる (#180)
            webView.CoreWebView2.WebResourceResponseReceived += (s, args) =>
            {
                if (!_composeReadyViews.Contains(webView)) return;
                var uri = args.Request.Uri;
                if (uri.Contains("/CreateTweet", StringComparison.OrdinalIgnoreCase) &&
                    args.Response.StatusCode >= 200 && args.Response.StatusCode < 300)
                {
                    DispatcherQueue.TryEnqueue(() => _activeComposeDialog?.Hide());
                }
            };

            webView.Source = new Uri("https://x.com/compose/post");
        }

        // ── Keyboard shortcuts ────────────────────────────────────────────────

        private void OnWebViewMessageReceived(WebView2 senderWebView, string message)
        {
            if (message.StartsWith("homeAutoLoad:"))
            {
                var status = message["homeAutoLoad:".Length..];
                if (_webViewToPane.TryGetValue(senderWebView, out var hp))
                    UpdateAutoLoadIndicator(hp, status);
                return;
            }

            if (message.StartsWith("focusIndex:") &&
                int.TryParse(message["focusIndex:".Length..], out var tlIndex))
            {
                FocusTimelineByIndex(tlIndex);  // Ctrl+数字 で N 番目をアクティブ化（#225）
                return;
            }

            if (message.StartsWith("openTimestamp:") &&
                Uri.TryCreate(message[14..], UriKind.Absolute, out var timestampUri))
            {
                _ = LaunchUriByEdgeProfileAsync(timestampUri);
                return;
            }

            switch (message)
            {
                case "focusNext": FocusAdjacentTimeline(senderWebView, +1); break;
                case "focusPrev": FocusAdjacentTimeline(senderWebView, -1); break;
                case "newPost":
                    if (_appSettings.OpenComposerInBrowser)
                        _ = LaunchUriByEdgeProfileAsync(new Uri("https://x.com/compose/post"));
                    else
                        _ = OpenPostDialogAsync(senderWebView);
                    break;
                case "focusSearch":
                    SearchBox.Focus(FocusState.Programmatic);
                    break;
                case "activate":
                    // ホイール操作したペインをアクティブ化（キーフォーカス移動）#221
                    if (_webViewToPane.TryGetValue(senderWebView, out var actPane) &&
                        _paneToSetFocus.TryGetValue(actPane, out var actSetFocus))
                        actSetFocus();
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
    }
}
