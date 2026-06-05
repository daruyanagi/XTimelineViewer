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
                Content         = webView,
                CloseButtonText = R.Get("Button_Close"),
                XamlRoot        = Content.XamlRoot,
            };

            var env = _appSettings.SeparateComposeEnv
                ? await GetOrCreateComposeEnvAsync()
                : await GetOrCreateProfileEnvAsync("default");
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
                await ShowDialogAsync(dlg);
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
            if (message.StartsWith("openTimestamp:") &&
                Uri.TryCreate(message[14..], UriKind.Absolute, out var timestampUri))
            {
                _ = Windows.System.Launcher.LaunchUriAsync(timestampUri);
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
    }
}
