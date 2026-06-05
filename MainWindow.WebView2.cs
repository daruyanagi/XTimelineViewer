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

namespace XTimelineViewer
{
    public sealed partial class MainWindow : Window
    {
        // ── WebView2 init ─────────────────────────────────────────────────────

        // グリフは Segoe Fluent Icons の私用領域(PUA)コードポイント。
        // 生の PUA 文字を直書きするとエンコーディング事故で欠落するため (#122)、
        // 必ず "\uXXXX" エスケープ表記で記述すること。
        private static string GetTimelineGlyph(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
            var p = uri.AbsolutePath;
            if (p.StartsWith("/home"))                                return "\uE80F"; // Home
            if (p.StartsWith("/notifications"))                       return "\uE7E7"; // Bell
            if (p.StartsWith("/search") || p.StartsWith("/explore")) return "\uE71E"; // Search
            if (p == "/bookmarks" || p.StartsWith("/bookmarks/") ||
                p == "/i/bookmarks" || p.StartsWith("/i/bookmarks/")) return "\uE734"; // Bookmark
            if (p.StartsWith("/i/lists/"))                            return "\uE71D"; // BulletedList
            if (p.StartsWith("/messages"))                            return "\uE8BD"; // Chat
            if (System.Text.RegularExpressions.Regex.IsMatch(p, @"^/[^/]+$")) return "\uE77B"; // Contact
            return "\uE774"; // Globe
        }

        private static bool IsProfilePath(string p) =>
            System.Text.RegularExpressions.Regex.IsMatch(p, @"^/[A-Za-z0-9_]+$") &&
            !p.StartsWith("/home") && !p.StartsWith("/notifications") &&
            !p.StartsWith("/search") && !p.StartsWith("/explore") &&
            !p.StartsWith("/bookmarks") && !p.StartsWith("/messages") &&
            !p.StartsWith("/i/");

        private static bool IsListHeaderApplicable(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            var p = uri.AbsolutePath;
            return p.StartsWith("/notifications") ||
                   p.StartsWith("/search")        ||
                   p.StartsWith("/explore")       ||
                   p == "/bookmarks" || p.StartsWith("/bookmarks/") ||
                   p == "/i/bookmarks" || p.StartsWith("/i/bookmarks/") ||
                   p.StartsWith("/i/lists/")      ||
                   IsProfilePath(p);
        }

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

        private void AddExtensionButton(CoreWebView2BrowserExtension ext, string extDir)
        {
            // マニフェストから名前、options_ui.page、アイコン、homepage_url を取得
            string name          = ext.Name;
            string? optPage      = null;
            string? iconPath     = null;
            string? homepageUrl  = null;
            var manifestPath = Path.Combine(extDir, "manifest.json");
            if (File.Exists(manifestPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("options_ui", out var optUi) &&
                    optUi.TryGetProperty("page", out var page))
                    optPage = page.GetString();
                if (root.TryGetProperty("icons", out var icons))
                {
                    foreach (var size in new[] { "16", "32", "48", "128" })
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

                // homepage_url がない場合、Chrome Web Store 拡張なら store URL を構成する
                if (homepageUrl is null &&
                    root.TryGetProperty("update_url", out var updateUrl) &&
                    updateUrl.GetString()?.Contains("clients2.google.com") == true)
                {
                    // ext.Id は WebView2 内部 ID のため、フォルダー名（元の CWS ID）を使う
                    homepageUrl = $"https://chromewebstore.google.com/detail/{Path.GetFileName(extDir)}";
                }
            }
            if (optPage is null) return; // options_ui がない拡張は追加しない

            object btnContent = iconPath is not null
                ? new Image
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconPath)),
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
            ToolTipService.SetToolTip(btn, string.Format(R.Get("ExtSettings_Format"), name));

            var optPageUrl = $"chrome-extension://{ext.Id}/{optPage}";
            btn.Click += async (_, _) =>
            {
                var optWebView = new WebView2 { Width = 480, MinHeight = 200 };

                Uri.TryCreate(homepageUrl, UriKind.Absolute, out var homepageUri);
                var linkText = homepageUri?.Host.Contains("chromewebstore.google.com") == true
                    ? R.Get("ExtSettings_StoreLink")
                    : R.Get("ExtSettings_Homepage");

                var dlg = new ContentDialog
                {
                    Title                = string.Format(R.Get("ExtSettings_Format"), name),
                    Content              = optWebView,
                    SecondaryButtonText  = homepageUri is not null ? linkText : null,
                    CloseButtonText      = R.Get("Button_Close"),
                    XamlRoot             = Content.XamlRoot
                };

                // SecondaryButton クリックでブラウザーを開き、ダイアログは閉じない
                if (homepageUri is not null)
                    dlg.SecondaryButtonClick += (s, e) =>
                    {
                        e.Cancel = true;
                        _ = Windows.System.Launcher.LaunchUriAsync(homepageUri);
                    };

                var env = await GetOrCreateProfileEnvAsync("default");
                await optWebView.EnsureCoreWebView2Async(env);
                optWebView.Source = new Uri(optPageUrl);
                await ShowDialogAsync(dlg);
            };

            // 設定ボタン（末尾）の左隣に挿入
            int insertIdx = Math.Max(0, RightToolbar.Children.Count - 1);
            RightToolbar.Children.Insert(insertIdx, btn);
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

        private async Task InitWebViewAsync(WebView2 webView, TimelineConfig cfg)
        {
            try
            {
                var env = await GetOrCreateProfileEnvAsync(cfg.ProfileId);
                await webView.EnsureCoreWebView2Async(env);
                webView.CoreWebView2.SourceChanged += (s, e) =>
                {
                    bool diverged = !IsOnBaseUrl(webView.CoreWebView2.Source, cfg.Url);
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
                                Text = $"EdgeDevAppDir: {EdgeDevAppDir}\n\nログ: {LogFilePath}\n\n{ex}",
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

            // 外部リンクをシステム既定ブラウザーで開く
            webView.CoreWebView2.NewWindowRequested += async (s, args) =>
            {
                args.Handled = true;
                await Windows.System.Launcher.LaunchUriAsync(new Uri(args.Uri));
            };

            webView.CoreWebView2.NavigationStarting += async (s, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var nav)) return;

                if (Uri.TryCreate(cfg.Url, UriKind.Absolute, out var origin) &&
                    !nav.Host.Equals(origin.Host, StringComparison.OrdinalIgnoreCase))
                {
                    args.Cancel = true;
                    await Windows.System.Launcher.LaunchUriAsync(nav);
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
