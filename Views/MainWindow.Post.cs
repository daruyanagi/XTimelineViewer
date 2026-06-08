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

        private async Task OpenPostDialogAsync(WebView2? senderWebView = null)
        {
            // ── プロファイルセレクターの構築 ──
            var selectedProfileId = ResolveComposeProfileId();

            var profileCombo = BuildProfileComboBox(selectedProfileId);
            var webView = new WebView2 { Width = 500, MinHeight = 520 };
            var rootPanel = new StackPanel { Spacing = 8 };
            rootPanel.Children.Add(profileCombo);
            rootPanel.Children.Add(webView);

            var dlg = new ContentDialog
            {
                Content         = rootPanel,
                CloseButtonText = R.Get("Button_Close"),
                XamlRoot        = Content.XamlRoot,
            };

            // ── WebView2 初期化 ──
            await InitComposeWebView(webView, selectedProfileId, dlg);

            // ── プロファイル切り替え ──
            profileCombo.SelectionChanged += async (s, args) =>
            {
                if (profileCombo.SelectedItem is not ComboBoxItem item) return;
                var newProfileId = item.Tag as string ?? "default";
                if (newProfileId == selectedProfileId) return;
                selectedProfileId = newProfileId;

                // 古い WebView2 を破棄して新しいものに差し替え
                var oldWebView = webView;
                webView = new WebView2 { Width = 500, MinHeight = 520 };
                rootPanel.Children.Remove(oldWebView);
                rootPanel.Children.Add(webView);
                try { oldWebView.Close(); } catch { }

                await InitComposeWebView(webView, selectedProfileId, dlg);
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
            }
        }

        /// <summary>投稿に使用するプロファイル ID を決定する。</summary>
        private string ResolveComposeProfileId()
        {
            // 前回使用したプロファイルが存在すればそれを使う
            if (_appSettings.LastUsedProfileId is { } last &&
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
            for (int i = 0; i < _profiles.Count; i++)
            {
                var profile = _profiles[i];
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
                if (profile.Id == selectedProfileId) selectedIndex = i;
            }

            combo.SelectedIndex = selectedIndex;
            return combo;
        }

        /// <summary>投稿用 WebView2 を初期化して compose/post へナビゲートする。</summary>
        private async Task InitComposeWebView(WebView2 webView, string profileId, ContentDialog dlg)
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
        }

        // ── Keyboard shortcuts ────────────────────────────────────────────────

        private void OnWebViewMessageReceived(WebView2 senderWebView, string message)
        {
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
