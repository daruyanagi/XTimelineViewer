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
        // ── WebView2 init ─────────────────────────────────────────────────────

        private static string BuildHideListHeaderJs(bool hide) => $$"""
            (function(hide){
                var id='xtv-hide-list-header';
                var s=document.getElementById(id);
                if(hide){
                    var path=window.location.pathname;
                    var css=[];
                    // 通知: 「通知」タイトル＋設定ギアを含むヘッダーブロックを非表示
                    // 直接子セレクタで深さを固定し、全祖先にマッチしないよう限定する
                    if(/^\/notifications/.test(path))
                        css.push('div:has(>div>div>div>div>div>a[data-testid="settingsAppBar"]){display:none!important}');
                    // 検索: 検索ボックス＋戻るボタンを含むヘッダーブロックを非表示
                    if(/^\/(search|explore)/.test(path))
                        css.push('div:has(>div>div>div>div>div>button[data-testid="app-bar-back"]){display:none!important}');
                    // リスト: 上部ナビバー＋リスト情報カード（バナー・作成者・メンバー数）を非表示
                    if(/^\/i\/lists\//.test(path)){
                        css.push('div:has(>div>div>div>div>div>button[data-testid="app-bar-back"]){display:none!important}');
                        css.push('[data-testid="cellInnerDiv"]:has(a[href*="/i/lists/"][href$="/members"]){display:none!important}');
                    }
                    // ブックマーク: 上部ナビバー＋タイトルブロック＋空フォルダの説明文を非表示
                    // nth-child(1) は X の DOM 変更で最初の投稿まで隠れる問題があったため
                    // :has(h2) でタイトル見出しを含む要素のみを対象にする (#115)
                    if(/^\/(i\/bookmarks|bookmarks)/.test(path)){
                        css.push('div:has(>div>div>div>div>div>button[data-testid="app-bar-back"]){display:none!important}');
                        css.push('#react-root main section>div>div>div:has(h2){display:none!important}');
                        css.push('[data-testid="emptyState"]{display:none!important}');
                    }
                    // プロフィール: ナビバー＋プロフィール情報カード（バナー・アバター・自己紹介・フォロー数）を非表示
                    if(/^\/[A-Za-z0-9_]+$/.test(path) &&
                       !/^\/(home|notifications|search|explore|bookmarks|messages|i)/.test(path)){
                        css.push('div:has(>div>div>div>div>div>button[data-testid="app-bar-back"]){display:none!important}');
                        css.push('div:has(>a[href$="/header_photo"]){display:none!important}');
                    }
                    if(css.length){
                        if(!s){s=document.createElement('style');s.id=id;document.head.appendChild(s);}
                        s.textContent=css.join('');
                    }else{
                        if(s)s.remove();
                    }
                }else{
                    if(s)s.remove();
                }
            })({{(hide ? "true" : "false")}});
            """;

        private static async Task ApplyHideListHeaderAsync(
            Microsoft.UI.Xaml.Controls.WebView2 webView, bool hide)
        {
            await webView.CoreWebView2.ExecuteScriptAsync(BuildHideListHeaderJs(hide));
        }

        private static string BuildHideSidebarJs(bool hide) => $$"""
            (function(hide){
                var id='xtv-hide-sidebar';
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

        private static async Task ApplyHideSidebarAsync(
            Microsoft.UI.Xaml.Controls.WebView2 webView, bool hide)
        {
            await webView.CoreWebView2.ExecuteScriptAsync(BuildHideSidebarJs(hide));
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

            // MSIX パッケージ内の extensions は WindowsApps 配下に置かれ WebView2 から直接アクセスできない。
            // LocalState へコピーしてから読み込む。アンパッケージド環境は BaseDirectory を使う。
            var extensionsDir = GetExtensionsDir();
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
                    Title           = R.Get("ExtLoadError_Title"),
                    Content         = new ScrollViewer
                    {
                        MaxHeight = 300,
                        Content   = new TextBlock
                        {
                            Text       = errors.ToString().TrimEnd()
                            + "\n\n" + webView.CoreWebView2.Environment.BrowserVersionString,
                            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                            FontSize   = 12,
                            IsTextSelectionEnabled = true,
                            TextWrapping = TextWrapping.Wrap
                        }
                    },
                    CloseButtonText = R.Get("Button_Close"),
                    XamlRoot        = Content.XamlRoot
                };
                await ShowDialogAsync(dlg);
            }
        }

        internal static ExtensionInfo ReadExtensionManifest(string extDir, string? extensionId = null, string? nameOverride = null)
        {
            string name     = nameOverride ?? Path.GetFileName(extDir);
            string? optPage     = null;
            string? iconPath    = null;
            string? homepageUrl = null;
            var manifestPath = Path.Combine(extDir, "manifest.json");
            if (File.Exists(manifestPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("name", out var nameProp) && nameOverride is null)
                    name = nameProp.GetString() ?? name;
                if (root.TryGetProperty("options_ui", out var optUi) &&
                    optUi.TryGetProperty("page", out var page))
                    optPage = page.GetString();
                if (root.TryGetProperty("icons", out var icons))
                {
                    foreach (var size in new[] { "48", "32", "128", "16" })
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
                if (root.TryGetProperty("homepage_url", out var hp))
                    homepageUrl = hp.GetString();
                if (homepageUrl is null &&
                    root.TryGetProperty("update_url", out var updateUrl) &&
                    updateUrl.GetString()?.Contains("clients2.google.com") == true)
                {
                    homepageUrl = $"https://chromewebstore.google.com/detail/{Path.GetFileName(extDir)}";
                }
            }
            return new ExtensionInfo(name, extDir, iconPath, optPage, homepageUrl, extensionId);
        }

        private void AddExtensionButton(CoreWebView2BrowserExtension ext, string extDir)
        {
            var info = ReadExtensionManifest(extDir, ext.Id, ext.Name);
            _loadedExtensions.Add(info);

            if (info.OptionsPage is null) return;

            object btnContent = info.IconPath is not null
                ? new Image
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(info.IconPath)),
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
            ToolTipService.SetToolTip(btn, string.Format(R.Get("ExtSettings_Format"), info.Name));

            btn.Click += async (_, _) =>
            {
                await ShowExtensionSettingsDialogAsync(info, Content.XamlRoot, LaunchUriByEdgeProfileAsync);
            };

            // 設定ボタン（末尾）の左隣に挿入
            int insertIdx = Math.Max(0, RightToolbar.Children.Count - 1);
            RightToolbar.Children.Insert(insertIdx, btn);
        }

        internal async Task ShowExtensionSettingsDialogAsync(
            ExtensionInfo info, Microsoft.UI.Xaml.XamlRoot xamlRoot, Func<Uri, Task> launchUri)
        {
            if (info.OptionsPage is null || info.ExtensionId is null) return;

            var optWebView = new WebView2 { Width = 480, MinHeight = 200 };

            Uri.TryCreate(info.HomepageUrl, UriKind.Absolute, out var homepageUri);
            var linkText = homepageUri?.Host.Contains("chromewebstore.google.com") == true
                ? R.Get("ExtSettings_StoreLink")
                : R.Get("ExtSettings_Homepage");

            var dlg = new ContentDialog
            {
                Title                = string.Format(R.Get("ExtSettings_Format"), info.Name),
                Content              = optWebView,
                SecondaryButtonText  = homepageUri is not null ? linkText : null,
                CloseButtonText      = R.Get("Button_Close"),
                XamlRoot             = xamlRoot
            };

            if (homepageUri is not null)
                dlg.SecondaryButtonClick += (s, e) =>
                {
                    e.Cancel = true;
                    _ = launchUri(homepageUri);
                };

            var env = await GetOrCreateProfileEnvAsync("default");
            await optWebView.EnsureCoreWebView2Async(env);
            var isDark = xamlRoot.Content is FrameworkElement fe
                && fe.ActualTheme == ElementTheme.Dark;
            optWebView.CoreWebView2.Profile.PreferredColorScheme = isDark
                ? CoreWebView2PreferredColorScheme.Dark
                : CoreWebView2PreferredColorScheme.Light;
            if (isDark)
            {
                optWebView.DefaultBackgroundColor = Windows.UI.Color.FromArgb(255, 32, 32, 32);
                optWebView.CoreWebView2.NavigationCompleted += async (s, e) =>
                {
                    await optWebView.CoreWebView2.ExecuteScriptAsync("""
                        if (!window.matchMedia('(prefers-color-scheme: dark)').matches ||
                            getComputedStyle(document.body).backgroundColor === 'rgb(255, 255, 255)') {
                            document.documentElement.style.cssText += 'background:#202020!important;color:#e0e0e0!important';
                            document.body.style.cssText += 'background:#202020!important;color:#e0e0e0!important';
                            document.querySelectorAll('input,select,textarea,button').forEach(el => {
                                el.style.cssText += 'background:#333!important;color:#e0e0e0!important;border-color:#555!important';
                            });
                        }
                    """);
                };
            }
            optWebView.Source = new Uri($"chrome-extension://{info.ExtensionId}/{info.OptionsPage}");
            await ShowDialogAsync(dlg);
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

        /// <summary>
        /// 現在アクティブなアカウントの X スクリーンネームをセッションからライブ取得する。
        /// 左ナビの「プロフィール」リンク（AppTabBar_Profile_Link）は委任アカウント切り替え後も
        /// アクティブなアカウントを指す。SPA のため NavigationCompleted 後に遅延描画されるので、
        /// 要素が現れるまで数回リトライする。取得できなければ（ログアウト等）null。
        /// </summary>
        private static async Task<string?> TryReadActiveScreenNameAsync(WebView2 webView, int attempts = 6)
        {
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    var result = await webView.CoreWebView2.ExecuteScriptAsync(
                        "document.querySelector('[data-testid=\"AppTabBar_Profile_Link\"]')?.href?.split('/').pop() ?? null");
                    if (result?.Trim('"') is { Length: > 0 } name && name != "null")
                        return name;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Profile] TryReadActiveScreenNameAsync failed: {ex.Message}");
                    return null;
                }
                await Task.Delay(700);
            }
            return null;
        }

        /// <summary>
        /// プロファイルの ScreenName が未保存なら、現在のセッションから補完する。
        /// リスト URL 解決の正の情報源はライブ取得（<see cref="EnsureListsUrlAsync"/>）だが、
        /// このキャッシュは初回ナビゲーションのちらつき低減用の初期推測として残している (#211)。
        /// </summary>
        private async Task BackfillScreenNameAsync(WebView2 webView, string profileId)
        {
            var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile is null || profile.ScreenName is { Length: > 0 }) return;

            var name = await TryReadActiveScreenNameAsync(webView);
            if (name is { Length: > 0 } && profile.ScreenName is not { Length: > 0 })
            {
                profile.ScreenName = name;
                SaveProfiles();
                Debug.WriteLine($"[Profile] ScreenName backfilled: {profile.Name} -> @{name}");
            }
        }

        /// <summary>
        /// リスト一覧タイムライン（<see cref="TimelineConfig.IsListsIndex"/>）の URL を、
        /// 現在アクティブなアカウントのハンドルでライブ解決する。委任アカウント切り替えにも追従する。
        /// 既に正しい URL ならナビゲートしない。
        /// </summary>
        private async Task EnsureListsUrlAsync(WebView2 webView, TimelineConfig cfg)
        {
            if (!cfg.IsListsIndex) return;

            var handle = await TryReadActiveScreenNameAsync(webView);
            if (handle is not { Length: > 0 }) return;  // ログアウト等は何もしない

            var target = BuildListsUrl(handle);
            if (UrlHelper.IsOnBaseUrl(webView.CoreWebView2.Source, target)) return;  // 既に正しい

            cfg.Url = target;
            if (_paneUrlUpdaters.TryGetValue(cfg, out var update)) update();
            await SaveTimelinesAsync();
            webView.Source = new Uri(target);
            Debug.WriteLine($"[Lists] Resolved active lists URL: {target}");
        }

        private async Task InitWebViewAsync(WebView2 webView, TimelineConfig cfg)
        {
            try
            {
                var env = await GetOrCreateProfileEnvAsync(cfg.ProfileId);
                await webView.EnsureCoreWebView2Async(env);
                webView.CoreWebView2.SourceChanged += (s, e) =>
                {
                    bool diverged = !UrlHelper.IsOnBaseUrl(webView.CoreWebView2.Source, cfg.Url);
                    if (diverged)
                    {
                        _urlDivergedWebViews.Add(webView);
                    }
                    else
                    {
                        _urlDivergedWebViews.Remove(webView);
                        if (Uri.TryCreate(cfg.Url, UriKind.Absolute, out var cfgU) &&
                            cfgU.AbsolutePath.StartsWith("/home", StringComparison.OrdinalIgnoreCase))
                            RestartAutoActivateTimer();
                    }
                    EvaluateHardReloadPause(webView);
                };
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
                        Title           = R.Get("WebViewInitError_Title"),
                        Content         = new ScrollViewer
                        {
                            MaxHeight = 300,
                            Content   = new TextBlock
                            {
                                Text = $"ログ: {LogFilePath}\n\n{ex}",
                                FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                                FontSize   = 12,
                                IsTextSelectionEnabled = true,
                                TextWrapping = TextWrapping.Wrap
                            }
                        },
                        CloseButtonText = R.Get("Button_Close"),
                        XamlRoot        = Content.XamlRoot
                    };
                    await ShowDialogAsync(dlg);
                }
                return;
            }



            // キーボードショートカット：ブラウザ既定アクセラレータを無効化し JS で代替処理
            webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(KeyboardShortcutScript);
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(TimestampInterceptScript);
            webView.CoreWebView2.WebMessageReceived += (s, e) =>
                OnWebViewMessageReceived(webView, e.TryGetWebMessageAsString());

            // 外部リンクをシステム既定ブラウザーまたは指定 Edge プロファイルで開く
            webView.CoreWebView2.NewWindowRequested += async (s, args) =>
            {
                args.Handled = true;
                await LaunchUriByEdgeProfileAsync(new Uri(args.Uri));
            };

            webView.CoreWebView2.NavigationStarting += async (s, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var nav)) return;

                if (Uri.TryCreate(cfg.Url, UriKind.Absolute, out var origin) &&
                    !nav.Host.Equals(origin.Host, StringComparison.OrdinalIgnoreCase))
                {
                    args.Cancel = true;
                    await LaunchUriByEdgeProfileAsync(nav);
                    return;
                }

            };

            webView.CoreWebView2.NavigationCompleted += async (s, args) =>
            {
                if (args.IsSuccess)
                {
                    await ApplyHideSidebarAsync(webView, cfg.HideSidebar);
                    await ApplyHideComposeAsync(webView, EffectiveHideCompose(cfg, webView.CoreWebView2.Source));
                    await ApplyHideListHeaderAsync(webView, cfg.HideListHeader);

                    var tsFlag = _appSettings.OpenTimestampInBrowser ? "true" : "false";
                    await webView.CoreWebView2.ExecuteScriptAsync(
                        $"window._xtvOpenTimestampInBrowser = {tsFlag};");

                    // x.com/home の場合だけ新着ポスト自動表示機能を適用する
                    if (Uri.TryCreate(webView.CoreWebView2.Source, UriKind.Absolute, out var current) &&
                        current.AbsolutePath.TrimEnd('/').Equals("/home", StringComparison.OrdinalIgnoreCase))
                    {
                        await ApplyAutoShowNewPostsAsync(webView, cfg.Url);
                    }

                    // プロファイルのスクリーンネームが未取得なら、ログイン中セッションから補完する
                    // （初期推測用のキャッシュ。リスト URL 解決の正は EnsureListsUrlAsync）。
                    await BackfillScreenNameAsync(webView, cfg.ProfileId);

                    // リスト一覧はアクティブアカウントのハンドルでライブ解決する（委任アカウント対応 #211）
                    await EnsureListsUrlAsync(webView, cfg);
                }
            };

            webView.CoreWebView2.SourceChanged += async (s, args) =>
            {
                if (cfg.HideCompose)
                    await ApplyHideComposeAsync(webView, EffectiveHideCompose(cfg, webView.CoreWebView2.Source));
                if (cfg.HideListHeader)
                    await ApplyHideListHeaderAsync(webView, cfg.HideListHeader);
            };

            webView.Source = new Uri(cfg.Url);
            StartHardReloadTimer(webView, cfg);
        }
    }
}
