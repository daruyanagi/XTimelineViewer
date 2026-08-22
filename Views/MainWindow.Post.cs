using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Windows.UI;

using XTimelineViewer.Models;

using XTimelineViewer.Views.Controls;

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
            foreach (var p in Panes)
                p.WebView.Visibility = Visibility.Collapsed;
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

                foreach (var p in Panes)
                    p.WebView.Visibility = Visibility.Visible;

                // 投稿アカウントの記憶。#285: 有効時は誤爆防止のため、次回はプライマリへ戻す。
                var nextProfileId = selectedProfileId;
                if (_appSettings.ComposeResetToPrimaryEnabled)
                {
                    var primary = _profiles.FirstOrDefault(p => p.IsPrimary)
                                  ?? _profiles.FirstOrDefault(p => p.Id != "default");
                    if (primary is not null) nextProfileId = primary.Id;
                }
                if (_appSettings.LastUsedProfileId != nextProfileId)
                {
                    _appSettings.LastUsedProfileId = nextProfileId;
                    SaveSettings();
                }

                // ダイアログを閉じた後、キーボードフォーカスを WebView2 に戻す
                var target = senderWebView is not null ? PaneOf(senderWebView) : Panes.FirstOrDefault();
                target?.SetFocus();

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
                        var s = document.getElementById(id);
                        if (!s) {
                            s = document.createElement('style');
                            s.id = id;
                            (document.head || document.documentElement).appendChild(s);
                        }

                        // 常に隠すもの（背景のタイムライン・サイドバー等）
                        var BASE =
                            '[data-testid="primaryColumn"],' +
                            '[data-testid="sidebarColumn"],' +
                            'header[role="banner"],' +
                            '[data-testid="modalBackdrop"]' +
                            '{display:none!important}';

                        // compose 上部バー（戻る・下書き・ポストする）を隠し、WinUI フッターに一本化 (#246)。
                        // ただしサブパネル（予約設定・位置情報・GIF 検索）は ✕ や確定ボタンが同じヘッダー行に
                        // 同居しており、隠すと確定も中断もできなくなる。そのため compose 本体を表示している
                        // ときだけ適用する (#330)。サブパネルは /compose/post 配下とは限らない
                        // （GIF は /i/foundmedia/search）ため、パスのホワイトリストで判定する。
                        var APPBAR =
                            '[data-testid="app-bar-close"],' +
                            '[data-testid="tweetButton"],' +
                            'div:has(> [data-testid="app-bar-close"]),' +
                            'div:has(> div > [data-testid="app-bar-close"])' +
                            '{display:none!important}';

                        function isComposeRoot() {
                            return /^\/compose\/(post|tweet)\/?$/.test(location.pathname);
                        }
                        function apply() {
                            var css = BASE + (isComposeRoot() ? APPBAR : '');
                            if (s.textContent !== css) s.textContent = css;
                        }
                        apply();

                        // SPA 遷移では NavigationCompleted が発火しないため、履歴 API をフックして追従する。
                        // 取りこぼし（X 内部の描画遅延など）に備えて低頻度のポーリングも併用する。
                        if (!window._xtvComposeRouteHook) {
                            window._xtvComposeRouteHook = true;
                            ['pushState', 'replaceState'].forEach(function (m) {
                                var orig = history[m];
                                history[m] = function () {
                                    var r = orig.apply(this, arguments);
                                    try { apply(); } catch (e) {}
                                    return r;
                                };
                            });
                            window.addEventListener('popstate', apply, true);
                            setInterval(apply, 500);
                        }
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
                if (PaneOf(senderWebView) is { } hp)
                    UpdateAutoLoadIndicator(hp, status);
                return;
            }

            if (message.StartsWith("editing:"))  // #258: いずれかのペインの編集状態
            {
                bool on = message == "editing:true";
                bool changed = on ? _composingWebViews.Add(senderWebView)
                                  : _composingWebViews.Remove(senderWebView);
                if (changed) UpdateAnyComposing();
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

            if (message.StartsWith("saveFrame:"))  // #299: 動画の現在フレームを PNG 保存
            {
                // 形式: saveFrame:<handle>|<status>|<base64>
                var parts = message["saveFrame:".Length..].Split('|', 3);
                if (parts.Length == 3)
                    _ = SaveVideoFrameAsync(senderWebView, parts[0], parts[1], parts[2]);
                return;
            }

            if (message.StartsWith("saveGif:"))  // #301: GIF(MP4) をダウンロード保存
            {
                // 形式: saveGif:<handle>|<status>|<url>（url は残り全部）
                var parts = message["saveGif:".Length..].Split('|', 3);
                if (parts.Length == 3)
                    _ = DownloadMediaAsync(senderWebView, parts[0], parts[1], parts[2], "mp4", "gifSaved");
                return;
            }

            if (message.StartsWith("saveImg:"))  // #304: 画像（高解像度）をダウンロード保存
            {
                var parts = message["saveImg:".Length..].Split('|', 3);
                if (parts.Length == 3)
                    _ = DownloadMediaAsync(senderWebView, parts[0], parts[1], parts[2], "jpg", "imgSaved");
                return;
            }

            if (message.StartsWith("saveVideo:"))  // #304: 動画を progressive MP4 でダウンロード保存
            {
                // 形式: saveVideo:<handle>|<status>（URL は GraphQL 傍受で保持した辞書から引く）
                var parts = message["saveVideo:".Length..].Split('|', 2);
                if (parts.Length == 2)
                {
                    var handle = parts[0];
                    var status = parts[1];
                    var hit = _videoMp4ByStatus.TryGetValue(status, out var mp4Url);
                    LogDebug($"saveVideo status={status} handle={handle} hit={hit} mapTotal={_videoMp4ByStatus.Count}");
                    if (hit)
                        _ = DownloadMediaAsync(senderWebView, handle, status, mp4Url!, "mp4", "videoSaved");
                    else
                        try { senderWebView.CoreWebView2?.PostWebMessageAsString("videoUnavailable"); } catch { }
                }
                return;
            }

            if (message.StartsWith("openFolder:"))  // #308: トーストの「フォルダーを開く」リンク
            {
                var dir = MediaDir(isVideo: message["openFolder:".Length..] == "videos");
                try
                {
                    Directory.CreateDirectory(dir);
                    Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
                }
                catch (Exception ex) { LogError("openFolder", ex); }
                return;
            }

            if (message == "openHelp")  // #310: 動画DL 失敗トーストの「対処法を見る」リンク
            {
                _ = LaunchUriByEdgeProfileAsync(new Uri("https://daruyanagi.jp/posts/2026/07/16/"));
                return;
            }

            if (message.StartsWith("searchPriorRepost:"))  // #315/#319: 直前のポスト・リポストを検索
            {
                // 形式: searchPriorRepost:<handle>|<T(unix秒)>
                var parts = message["searchPriorRepost:".Length..].Split('|', 2);
                if (parts.Length == 2 && long.TryParse(parts[1], out var t))
                {
                    // handle は英数と _ のみに安全化
                    var handle = new string(parts[0].Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
                    if (handle.Length > 0)
                    {
                        // include:nativeretweets で自ポストとリポストを混在させ、発言の文脈を時系列で遡る（#319）
                        var q = $"from:{handle} include:nativeretweets until_time:{t}";
                        var path = "/search?q=" + Uri.EscapeDataString(q) + "&f=live";
                        // 既存の検索 UI を再利用（非破壊・履歴を汚さない）。閲覧目的なので追加ボタンは出さない。
                        _ = OpenSearchDialogAsync(path, showAddButton: false);
                    }
                }
                return;
            }

            switch (message)
            {
                case "focusNext": FocusAdjacentTimeline(senderWebView, +1); break;
                case "focusPrev": FocusAdjacentTimeline(senderWebView, -1); break;
                // Ctrl+Shift+←/→ でペインを並べ替える（#344）
                case "movePaneNext": MoveTimelinePane(senderWebView, +1); break;
                case "movePanePrev": MoveTimelinePane(senderWebView, -1); break;
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
                    PaneOf(senderWebView)?.SetFocus();
                    // webView.Focus() だけでは document に実フォーカスが移らず Home 等が効かない
                    // ことがあるため、document.hasFocus が立つまでリトライして確実にする（#251）
                    EnsureWebViewKeyboardFocus(senderWebView);
                    break;
            }
        }

        // 動画の現在フレーム（base64 PNG）を Pictures\XTimelineViewer に保存する（#299・試験機能）。
        // ファイル名は <handle>_<statusId>_<保存時刻> で元ポスト（x.com/handle/status/statusId）を辿れる。
        private async Task SaveVideoFrameAsync(WebView2 senderWebView, string handle, string status, string base64Png)
        {
            string? savedName = null;
            try
            {
                var bytes = Convert.FromBase64String(base64Png);
                var dir = MediaDir(isVideo: false);  // フレームは PNG 画像 → Pictures
                Directory.CreateDirectory(dir);
                // 保存時刻はミリ秒まで付けて同一秒内の連続保存でも衝突しないようにする。
                var name = $"{SanitizeNamePart(handle, "unknown")}_{SanitizeNamePart(status, "noid")}"
                         + $"_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
                await File.WriteAllBytesAsync(Path.Combine(dir, name), bytes);
                savedName = name;
            }
            catch (Exception ex)
            {
                LogError("SaveVideoFrame", ex);
            }

            // 保存結果を WebView へ返してトースト表示させる（全画面中は XAML ダイアログが
            // WebView2 の裏に隠れるため、フィードバックはページ内で出す）。
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    senderWebView.CoreWebView2?.PostWebMessageAsString(
                        savedName is null ? "frameError" : "frameSaved:" + savedName);
                }
                catch { }
            });
        }

        private static readonly System.Net.Http.HttpClient _frameHttp = new();

        // 動画ダウンロード用（#304）: statusId → progressive MP4（最高ビットレート）直 URL。
        // X の GraphQL レスポンスを傍受して populate する。ffmpeg を使わず音声込み mp4 を得るための要。
        private readonly ConcurrentDictionary<string, string> _videoMp4ByStatus = new();

        // GraphQL レスポンスから tweet の video_info.variants を拾い、statusId→最高画質 MP4 を辞書化する（#304）。
        // WebResourceResponseReceived から呼ぶ。失敗しても無視（動画DL 側で「取得できません」に degrade）。
        internal async Task CaptureVideoVariantsAsync(CoreWebView2WebResourceResponseReceivedEventArgs args)
        {
            // 一時診断（動画DL 調査）: 操作名を URL 末尾から抜く。
            var uri = args.Request.Uri;
            var op = uri;
            var q = op.IndexOf('?'); if (q >= 0) op = op[..q];
            var slash = op.LastIndexOf('/'); if (slash >= 0) op = op[(slash + 1)..];
            try
            {
                using var raStream = await args.Response.GetContentAsync();
                if (raStream is null) { LogDebug($"gql {op} status={args.Response.StatusCode} content=NULL"); return; }
                byte[] bytes;
                using (var netStream = raStream.AsStreamForRead())
                using (var ms = new MemoryStream())
                {
                    await netStream.CopyToAsync(ms);
                    bytes = ms.ToArray();
                }
                if (bytes.Length == 0) { LogDebug($"gql {op} status={args.Response.StatusCode} content=EMPTY"); return; }
                var pairs = await Task.Run(() =>
                {
                    var list = new List<(string id, string url)>();
                    try
                    {
                        using var doc = JsonDocument.Parse(bytes);
                        CollectVideoVariants(doc.RootElement, list);
                    }
                    catch { /* JSON でない/スキーマ違い等は無視 */ }
                    return list;
                });
                foreach (var (id, url) in pairs) _videoMp4ByStatus[id] = url;
                // 診断: 応答に video_info / mp4変種 が含まれるか（パーサ漏れか、そもそも含まれないかの判別）。
                bool hasVI = false, hasMp4 = false; string snip = "";
                try
                {
                    var text = System.Text.Encoding.UTF8.GetString(bytes);
                    hasVI = text.Contains("video_info");
                    hasMp4 = text.Contains("video/mp4");
                    if (hasVI && pairs.Count == 0)
                    {
                        var idx = text.IndexOf("video_info", StringComparison.Ordinal);
                        var from = Math.Max(0, idx - 40);
                        snip = text.Substring(from, Math.Min(320, text.Length - from)).Replace("\n", " ").Replace("\r", "");
                    }
                }
                catch { }
                LogDebug($"gql {op} bytes={bytes.Length} hasVI={hasVI} hasMp4={hasMp4} videos={pairs.Count} mapTotal={_videoMp4ByStatus.Count}"
                       + (pairs.Count > 0 ? " ids=" + string.Join(",", pairs.Select(p => p.id).Take(5)) : "")
                       + (snip != "" ? " SNIP=" + snip : ""));
            }
            catch (Exception ex)
            {
                LogDebug($"gql {op} EXCEPTION {ex.GetType().Name}: {ex.Message}");
                LogError("CaptureVideoVariants", ex);
            }
        }

        // rest_id + legacy を持つ tweet オブジェクトを再帰探索し、最高ビットレートの video/mp4 を集める。
        private static void CollectVideoVariants(JsonElement el, List<(string, string)> outp)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    if (el.TryGetProperty("rest_id", out var idEl) && idEl.ValueKind == JsonValueKind.String &&
                        el.TryGetProperty("legacy", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
                    {
                        var url = BestMp4FromLegacy(legacy);
                        if (url is not null) outp.Add((idEl.GetString()!, url));
                    }
                    foreach (var p in el.EnumerateObject()) CollectVideoVariants(p.Value, outp);
                    break;
                case JsonValueKind.Array:
                    foreach (var it in el.EnumerateArray()) CollectVideoVariants(it, outp);
                    break;
            }
        }

        private static string? BestMp4FromLegacy(JsonElement legacy)
        {
            JsonElement media = default;
            bool has =
                (legacy.TryGetProperty("extended_entities", out var ee) &&
                 ee.TryGetProperty("media", out media) && media.ValueKind == JsonValueKind.Array) ||
                (legacy.TryGetProperty("entities", out var en) &&
                 en.TryGetProperty("media", out media) && media.ValueKind == JsonValueKind.Array);
            if (!has) return null;

            string? best = null;
            long bestBr = -1;
            foreach (var m in media.EnumerateArray())
            {
                if (!m.TryGetProperty("video_info", out var vi)) continue;
                if (!vi.TryGetProperty("variants", out var vars) || vars.ValueKind != JsonValueKind.Array) continue;
                foreach (var v in vars.EnumerateArray())
                {
                    if (v.TryGetProperty("content_type", out var ct) && ct.ValueKind == JsonValueKind.String &&
                        ct.GetString() == "video/mp4" &&
                        v.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                    {
                        long br = 0;
                        if (v.TryGetProperty("bitrate", out var b) && b.ValueKind == JsonValueKind.Number &&
                            b.TryGetInt64(out var bv)) br = bv;
                        if (br > bestBr) { bestBr = br; best = u.GetString(); }
                    }
                }
            }
            return best;
        }

        // WebView へメッセージを返す（UI スレッドで PostWebMessageAsString）。トースト/進捗の通知用。
        private void PostToWebView(WebView2 webView, string message)
            => DispatcherQueue.TryEnqueue(() =>
            {
                try { webView.CoreWebView2?.PostWebMessageAsString(message); } catch { }
            });

        // twimg.com の直 URL からメディア（GIF/画像/動画）をダウンロードして保存する（#301/#304・試験機能）。
        // 安全のため twimg.com ホストのみ許可。ファイル名は <handle>_<statusId>_<時刻>.<ext>。
        // ストリーム読みしながら進捗（dlProgress:<pct>）を返し、完了時に okPrefix、失敗時に frameError を返す（#308）。
        private async Task DownloadMediaAsync(
            WebView2 senderWebView, string handle, string status, string url, string ext, string okPrefix)
        {
            string? savedName = null;
            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttps) &&
                    (uri.Host == "video.twimg.com" || uri.Host.EndsWith(".twimg.com")))
                {
                    var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, uri);
                    req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
                    using var resp = await _frameHttp
                        .SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead)
                        .ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();

                    var total = resp.Content.Headers.ContentLength;
                    byte[] bytes;
                    using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var buf = new MemoryStream())
                    {
                        var chunk = new byte[81920];
                        long readTotal = 0;
                        int lastPct = -1, n;
                        while ((n = await src.ReadAsync(chunk).ConfigureAwait(false)) > 0)
                        {
                            buf.Write(chunk, 0, n);
                            readTotal += n;
                            if (total is long tot && tot > 0)
                            {
                                int pct = (int)(readTotal * 100 / tot);
                                if (pct >= lastPct + 2 || pct == 100)  // 2% 刻みでスロットル
                                {
                                    lastPct = pct;
                                    PostToWebView(senderWebView, "dlProgress:" + pct);
                                }
                            }
                        }
                        bytes = buf.ToArray();
                    }

                    var dir = MediaDir(isVideo: ext == "mp4");  // 動画/GIF(mp4)→Videos, 画像(jpg)→Pictures
                    Directory.CreateDirectory(dir);
                    var name = $"{SanitizeNamePart(handle, "unknown")}_{SanitizeNamePart(status, "noid")}"
                             + $"_{DateTime.Now:yyyyMMdd_HHmmss_fff}.{ext}";
                    await File.WriteAllBytesAsync(Path.Combine(dir, name), bytes).ConfigureAwait(false);
                    savedName = name;
                }
            }
            catch (Exception ex)
            {
                LogError($"DownloadMedia({okPrefix})", ex);
            }

            PostToWebView(senderWebView, savedName is null ? "frameError" : okPrefix + ":" + savedName);
        }

        // メディア保存先（#308）: 動画/GIF(mp4)は Videos\XTimelineViewer、画像/フレームは Pictures\XTimelineViewer。
        private static string MediaDir(bool isVideo)
        {
            var special = isVideo ? Environment.SpecialFolder.MyVideos : Environment.SpecialFolder.MyPictures;
            return Path.Combine(Environment.GetFolderPath(special), "XTimelineViewer");
        }

        // ファイル名の一部（handle / statusId）を安全化する。空ならフォールバックを返す。
        private static string SanitizeNamePart(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            foreach (var c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value;
        }

        // WebView2 コンテンツに実際のキーボードフォーカスを確実に当てる（#251）。
        // webView.Focus() は true を返しても document.hasFocus が立たないことがあるため、
        // window.focus() と併せて document.hasFocus が true になるまで短時間リトライする。
        private void EnsureWebViewKeyboardFocus(WebView2 webView)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                if (webView.CoreWebView2 is null) return;
                for (int i = 0; i < 10; i++)
                {
                    webView.Focus(FocusState.Programmatic);
                    string r;
                    try { r = await webView.CoreWebView2.ExecuteScriptAsync("(function(){try{window.focus();}catch(e){} return document.hasFocus();})()"); }
                    catch { return; }
                    if (r == "true") return;  // フォーカス確定
                    await Task.Delay(60);
                }
            });
        }

        // 送信元のペインを隣と入れ替える（#344）
        private void MoveTimelinePane(WebView2 senderWebView, int direction)
        {
            if (PaneOf(senderWebView) is { } pane)
                MovePaneAdjacent(pane, direction);
        }

        private void FocusAdjacentTimeline(WebView2 senderWebView, int direction)
        {
            if (PaneOf(senderWebView) is not { } senderPane) return;
            int idx  = TimelinePanel.Children.IndexOf(senderPane);
            int next = idx + direction;
            if (next < 0 || next >= TimelinePanel.Children.Count) return;
            var targetPane = (TimelinePane)TimelinePanel.Children[next];
            targetPane.SetFocus();
            targetPane.StartBringIntoView();
        }
    }
}
