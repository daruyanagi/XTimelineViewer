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
        private Action<int>? _composeCycleProfile;               // 投稿アカウントを巡回（#247 Ctrl+P=+1 / #252 Ctrl+Shift+P=-1）
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

            // Ctrl+P（#247）次へ / Ctrl+Shift+P（#252）前へ。コンボの選択を進める/戻すと
            // SelectionChanged が発火してプロファイルが切り替わる。
            _composeCycleProfile = dir =>
            {
                int count = profileCombo.Items.Count;
                if (count <= 1) return;
                profileCombo.SelectedIndex = (profileCombo.SelectedIndex + dir + count) % count;
            };

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
                Content  = rootPanel,
                XamlRoot = Content.XamlRoot,
            };
            _activeComposeDialog = dlg;
            // 表示されたら編集ボックスにフォーカス（プリロード済みは既にロード完了しているのでここで効く）#246
            dlg.Opened += (_, _) => FocusComposeEditorDeferred(webView);

            // ── フッター（#246）：左 ［キャンセルして閉じる］(ESC) / 右 ［投稿する］(Ctrl+Enter・強調色) ──
            // 一般的な Windows 作法とは左右が逆だが、X の作法（投稿は右）に寄せる。
            var cancelBtn = new Button
            {
                Content             = R.Get("Compose_Cancel"),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            cancelBtn.Click += (_, _) => dlg.Hide();
            cancelBtn.KeyboardAccelerators.Add(
                new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape });

            var postBtn = new Button
            {
                Content             = R.Get("Compose_Post"),  // 「投稿する（Ctrl+Enter）」
                HorizontalAlignment = HorizontalAlignment.Right,
                Style               = (Style)Application.Current.Resources["AccentButtonStyle"],
            };
            // 投稿は WebView へ Ctrl+Enter を送信して X に任せる（成功時のクローズは #180 の監視に委譲）
            postBtn.Click += async (_, _) => await TriggerComposePostAsync(webView);

            var footer = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(cancelBtn, 0);
            Grid.SetColumn(postBtn, 1);
            footer.Children.Add(cancelBtn);
            footer.Children.Add(postBtn);
            rootPanel.Children.Add(footer);

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
                rootPanel.Children.Insert(1, webView);  // profileCombo(0) / webView(1) / footer(2) の順を保つ
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
                _composeCycleProfile = null;

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

        // 「投稿する」ボタン（#246）：X の投稿ボタンを click して投稿させる。
        // CSS で非表示にしていても .click() は有効。合成 KeyboardEvent(Ctrl+Enter) は
        // X 側ハンドラが信頼イベントでないため効かないので、ボタン click 方式にする。
        // （ユーザーがキーボードで Ctrl+Enter を押した場合は X がネイティブに投稿する。）
        private static async Task TriggerComposePostAsync(WebView2 webView)
        {
            if (webView.CoreWebView2 is null) return;
            await webView.CoreWebView2.ExecuteScriptAsync("""
                (function () {
                    var b = document.querySelector('[data-testid="tweetButton"]')
                         || document.querySelector('[data-testid="tweetButtonInline"]');
                    if (b) b.click();
                })();
                """);
        }

        // ダイアログ表示時に編集ボックス（X のコンポーザー）へフォーカスする（#246）。
        // ContentDialog が開いた直後に自前要素へフォーカスを移すため、低優先度で遅延実行する。
        // X エディタは直後は focus を受け付けないことがあるので JS 側で短時間リトライする。
        private void FocusComposeEditorDeferred(WebView2 webView)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                if (webView.CoreWebView2 is null) return;
                // ダイアログ表示直後は WebView の document に OS フォーカスが来ないことがあるため、
                // コントロール/ウィンドウ/要素のフォーカスを document.hasFocus が立つまでリトライする。
                for (int i = 0; i < 12; i++)
                {
                    webView.Focus(FocusState.Programmatic);
                    string r;
                    try
                    {
                        r = await webView.CoreWebView2.ExecuteScriptAsync("""
                            (function () {
                                try { window.focus(); } catch (_) {}
                                var el = document.querySelector('.public-DraftEditor-content')
                                      || document.querySelector('[role="textbox"]')
                                      || document.querySelector('[data-testid="tweetTextarea_0"]');
                                if (el) el.focus();
                                var a = document.activeElement;
                                return JSON.stringify({ hf: document.hasFocus(), ok: !!el && (a === el || el.contains(a)) });
                            })();
                            """);
                    }
                    catch { return; }
                    if (r.Contains("\"ok\":true")) return;  // フォーカス成功
                    await Task.Delay(80);
                }
            });
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

            // ブラウザー既定アクセラレータ（Ctrl+P の印刷など）を無効化し、JS で拾えるようにする（#247）
            webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

            // WebView 内 JS からの通知を処理（#246 ESC / #247 アカウント切替）
            webView.CoreWebView2.WebMessageReceived += (s, e) =>
            {
                switch (e.TryGetWebMessageAsString())
                {
                    case "composeCancel":
                        DispatcherQueue.TryEnqueue(() => _activeComposeDialog?.Hide());
                        break;
                    case "composeNextProfile":
                        DispatcherQueue.TryEnqueue(() => _composeCycleProfile?.Invoke(+1));
                        break;
                    case "composePrevProfile":  // #252
                        DispatcherQueue.TryEnqueue(() => _composeCycleProfile?.Invoke(-1));
                        break;
                }
            };

            // テーマを適用
            var root = (FrameworkElement)Content;
            var scheme = root.ActualTheme switch
            {
                ElementTheme.Light => CoreWebView2PreferredColorScheme.Light,
                ElementTheme.Dark  => CoreWebView2PreferredColorScheme.Dark,
                _                  => CoreWebView2PreferredColorScheme.Auto,
            };
            webView.CoreWebView2.Profile.PreferredColorScheme = scheme;

            // 離脱確認（beforeunload）の抑止（#246）。キャンセルやリセット時の再ナビゲートで
            // 「サイトから移動しますか?」が出ないよう、X より先（document 作成時）に
            // キャプチャ段階の beforeunload リスナーを登録して伝播を止める。
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("""
                (function () {
                    window.addEventListener('beforeunload', function (e) {
                        e.stopImmediatePropagation();
                        delete e['returnValue'];
                    }, true);
                    // ESC はアプリ側でダイアログを閉じる。X に渡すと compose から SPA 遷移して
                    // 黒画面になるため、ここで捕捉して伝播を止める（#246）。
                    window.addEventListener('keydown', function (e) {
                        if (e.key === 'Escape') {
                            e.preventDefault();
                            e.stopImmediatePropagation();
                            try { window.chrome.webview.postMessage('composeCancel'); } catch (_) {}
                            return;
                        }
                        // Ctrl+P で次／Ctrl+Shift+P で前の投稿アカウントへ（#247 / #252）。印刷ダイアログは抑止。
                        if (e.ctrlKey && !e.altKey && (e.key === 'p' || e.key === 'P')) {
                            e.preventDefault();
                            e.stopImmediatePropagation();
                            var msg = e.shiftKey ? 'composePrevProfile' : 'composeNextProfile';
                            try { window.chrome.webview.postMessage(msg); } catch (_) {}
                        }
                    }, true);
                })();
                """);

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
                            '[data-testid="modalBackdrop"],' +
                            // compose 上部バー（戻る・下書き・ポストする）を隠し、WinUI フッターに一本化 (#246)
                            '[data-testid="app-bar-close"],' +
                            '[data-testid="tweetButton"],' +
                            'div:has(> [data-testid="app-bar-close"]),' +
                            'div:has(> div > [data-testid="app-bar-close"])' +
                            '{display:none!important}';
                        document.head.appendChild(s);
                    })();
                    """);

                // オンデマンド生成時：ダイアログ表示中ならロード完了後に編集ボックスへフォーカス（#246）
                if (_activeComposeDialog is not null)
                    FocusComposeEditorDeferred(webView);
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
