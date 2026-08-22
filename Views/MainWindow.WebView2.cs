using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.UI;

using XTimelineViewer.Models;
using XTimelineViewer.Services;

using XTimelineViewer.Views.Controls;

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
                    // ブックマーク: 上部ナビバー＋空フォルダの説明文を非表示。
                    // X の再編で /i/bookmarks は /i/history にリダイレクトされ、ブックマークは
                    // 「履歴」ページ配下のタブ（ブックマーク/いいね）になった。パス判定に
                    // /i/history を追加する (#329)。旧パスもリダイレクト元として残しておく。
                    // 「履歴」見出しは app-bar-back を含むヘッダーブロック側に入るため、
                    // 従来の :has(h2) セレクタ（#115）は不要になった。タブ行は別ブロックなので
                    // 巻き込まれず、ブックマーク/いいねの切り替えは残る。
                    if(/^\/(i\/history|i\/bookmarks|bookmarks)/.test(path)){
                        css.push('div:has(>div>div>div>div>div>button[data-testid="app-bar-back"]){display:none!important}');
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

        // 編集状態レポーター（#258）。全ペインに注入し、編集中（モーダル表示／編集要素フォーカス）に
        // なったら postMessage('editing:true'/'editing:false') で C# に通知する。C# 側は「いずれかの
        // ペインが編集中」を集約してホーム自動更新を一時停止する（別ペインの下書き消失を防ぐ）。
        private static readonly string EditStateReporterScript = ScriptLoader.Get("EditStateReporter");

        // メディア拡大ボタンのオーバーレイ（試験機能 #293）。全ペインに注入し、タイムライン上の
        // 画像・動画コンテナに「⛶」ボタンを重ねる。押すとメディアを全画面表示し（＝メディアだけに
        // フォーカス）、WebView2 の ContainsFullScreenElement が立って既存の全画面フック（#291）で
        // ペインが画面いっぱいに拡大される。全画面中は「✕」ボタンを重ね、Esc とあわせて戻れる。
        // window._xtvMediaOverlayEnabled で ON/OFF を制御する。
        //   ・画像（#295）: 内部 <img> を専用ビューア div に入れて全画面化する。div は背景黒・
        //     object-fit:contain なので、コンテナごと全画面にしていた頃の上下見切れが起きない。
        //     さらに src の name=... を orig へ差し替えて拡大時だけ高解像度版を読み込む。
        //   ・動画: 従来どおりコンテナを全画面化してカスタムコントロールを保つ。
        private static readonly string MediaOverlayButtonScript = ScriptLoader.Get("MediaOverlayButton");

        /// <summary>現在の設定を、メディア拡大ボタンの JS 制御変数へ反映するスニペット（#293）。
        /// フレーム保存（#299）のローカライズ文言もここで JS へ渡す。</summary>
        private string BuildMediaOverlayButtonConfigJs()
        {
            var labels = System.Text.Json.JsonSerializer.Serialize(new
            {
                tip              = R.Get("MediaFrameSave_Tooltip"),
                saved            = R.Get("MediaFrameSave_Saved"),
                failed           = R.Get("MediaFrameSave_Failed"),
                gifTip           = R.Get("MediaFrameSave_GifTooltip"),
                gifSaved         = R.Get("MediaFrameSave_GifSaved"),
                dlTip            = R.Get("MediaFrameSave_DownloadTooltip"),
                imgSaved         = R.Get("MediaFrameSave_ImgSaved"),
                videoSaved       = R.Get("MediaFrameSave_VideoSaved"),
                videoUnavailable = R.Get("MediaFrameSave_VideoUnavailable"),
                openFolder       = R.Get("MediaFrameSave_OpenFolder"),
                downloading      = R.Get("MediaFrameSave_Downloading"),
                help             = R.Get("MediaFrameSave_HelpLink"),
            });
            return $"window._xtvMediaOverlayEnabled = {(_appSettings.MediaOverlayButtonEnabled ? "true" : "false")};"
                 + $"window._xtvFrameSaveEnabled = {(_appSettings.VideoFrameSaveEnabled ? "true" : "false")};"
                 + $"window._xtvFrameSaveL = {labels};";
        }

        /// <summary>メディア拡大ボタンの ON/OFF を各ペインへ即時反映する（#293）。</summary>
        private async Task ApplyMediaOverlayButtonAsync(WebView2 webView)
        {
            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(BuildMediaOverlayButtonConfigJs());
                await webView.CoreWebView2.ExecuteScriptAsync("window._xtvMediaBtnRescan && window._xtvMediaBtnRescan();");
            }
            catch { /* ページ遷移中や破棄済みだと落ちる。常態なので記録するとノイズになる */ }
        }

        // ポストのタイムスタンプ隣に「直前のポスト・リポストを検索」ボタン（🔎）を添える（試験機能 #315/#319）。
        // ［…］メニューへの注入はフォロー解除/ブロック等と隣接して誤爆しやすく、メニュー DOM 依存で
        // 壊れやすいので、タイムスタンプの直後に自前ボタンを置く方式にした。ホバーで薄く現れる。
        // クリックで C# へ searchPriorRepost:<handle>|<T(unix秒)> を送る。
        private static readonly string PriorRepostSearchScript = ScriptLoader.Get("PriorRepostSearch");

        /// <summary>「直前のリポストを検索」の ON/OFF とボタン文言を JS へ渡す（#315）。</summary>
        private string BuildPriorRepostConfigJs()
            => $"window._xtvPriorRepostEnabled = {(_appSettings.PriorRepostSearchEnabled ? "true" : "false")};"
             + $"window._xtvPriorRepostLabel = {System.Text.Json.JsonSerializer.Serialize(R.Get("PriorRepostSearch_ButtonLabel"))};";

        /// <summary>「直前のリポストを検索」の設定を各ペインへ即時反映する（#315）。</summary>
        private async Task ApplyPriorRepostSearchAsync(WebView2 webView)
        {
            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(BuildPriorRepostConfigJs());
                await webView.CoreWebView2.ExecuteScriptAsync("window._xtvPriorScan && window._xtvPriorScan();");
            }
            catch { /* ページ遷移中や破棄済みだと落ちる。常態なので記録するとノイズになる */ }
        }

        /// <summary>cfg がホームタイムラインかどうか。</summary>
        private static bool IsHomeConfig(TimelineConfig cfg) => UrlHelper.IsHomeUrl(cfg.Url);

        // ホームタイムライン自動更新（#207）。同梱拡張 TwitterTimelineLoader（TLLoader_main.js）の
        // ロジックをできるだけ忠実に移植。/home でページ先頭にいるとき一定間隔で
        // ホームタブ（a[data-testid="AppTabBar_Home_Link"]）を click して新着を取り込む。
        // 変更点: chrome.storage 依存を撤去し、window._xtvHomeAutoLoadEnabled / _xtvHomeAutoLoadIntervalMs で制御。
        //         状態（稼働中/一時停止/オフ）を postMessage('homeAutoLoad:...') でアプリに通知し、
        //         ヘッダーのインジケーターへ反映する。参考: https://qiita.com/ryounagaoka/items/a48d3a4c4faf78a99ae5
        private static readonly string HomeAutoLoadScript = ScriptLoader.Get("HomeAutoLoad");

        /// <summary>現在の設定（ON/OFF・間隔）を JS の制御変数へ反映するスニペット。</summary>
        private string BuildHomeAutoLoadConfigJs()
        {
            var enabled = _appSettings.HomeAutoLoadEnabled ? "true" : "false";
            var ms = Math.Max(5, _appSettings.HomeAutoLoadIntervalSeconds) * 1000;
            return $"window._xtvHomeAutoLoadEnabled = {enabled}; window._xtvHomeAutoLoadIntervalMs = {ms};";
        }

        /// <summary>注入済みのホーム自動更新スクリプトへ現在の設定を即時反映する。</summary>
        private async Task ApplyHomeAutoLoadAsync(WebView2 webView)
        {
            try { await webView.CoreWebView2.ExecuteScriptAsync(BuildHomeAutoLoadConfigJs()); }
            catch { /* ページ遷移中や破棄済みだと落ちる。常態なので記録するとノイズになる */ }
        }

        /// <summary>いずれかのペインが編集中かを全ペインの JS（window._xtvAnyComposing）へ反映する（#258）。</summary>
        private void UpdateAnyComposing()
        {
            var any = _composingWebViews.Count > 0 ? "true" : "false";
            foreach (var pane in Panes)
                if (pane.WebView.CoreWebView2 is not null)
                    pane.WebView.CoreWebView2.ExecuteScriptAsync($"window._xtvAnyComposing = {any};").AsTask().FireAndForget("ExecuteScript");
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

            // manifest.json を持たないフォルダーは WebView2 が受け付けない。
            // 展開しそこなった残骸などを拾って、毎回エラーを出さないようにする。
            foreach (var extDir in ExtensionStore.EnumerateExtensionDirs(extensionsDir))
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

        // 実体は Services/AppLog.cs（#374）。以前はここと App.xaml.cs に
        // 同じ error.log へ書く実装が別々にあり、パスも書式も揃っていなかった。
        private static string LogFilePath => AppLog.FilePath;

        private static void LogError(string context, Exception ex) => AppLog.Error(context, ex);

        // 一時診断用（動画DL の GraphQL 働受調査、#310）。
        private static void LogDebug(string msg) => AppLog.Debug(msg);

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
            PaneOf(webView)?.UpdateUrlHeader();
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
                    }
                    EvaluateHardReloadPause(webView);

                    // 画像表示中はペインを一時拡大する（試験機能 #287）
                    if (_appSettings.MediaEnlargeEnabled &&
                        PaneOf(webView) is { } pane)
                    {
                        if (UrlHelper.IsMediaPhotoUrl(webView.CoreWebView2.Source))
                            EnlargePane(pane);
                        else if (_enlargedPane == pane)
                            RestorePaneSize();
                    }
                };

                // 動画の全画面ボタン（試験機能 #289）。ページが HTML 全画面 API を要求すると発火する。
                // 既定では WebView2 は自コントロール内で全画面表示するため、細いペイン内に収まって
                // 戻る導線が失われる。要求を検知してペインごと拡大し、全画面解除で元に戻す。
                // ユーザーが全画面ボタンを押したときだけ発火するので、動画の自動再生を誤検知しない。
                // 動画の全画面ボタン（#289）に加え、メディア拡大ボタン（#293）から requestFullscreen した
                // ときもここに合流する。どちらのトグルも OFF なら何もしない。
                webView.CoreWebView2.ContainsFullScreenElementChanged += (s, e) =>
                {
                    if (!_appSettings.VideoEnlargeEnabled && !_appSettings.MediaOverlayButtonEnabled) return;
                    if (PaneOf(webView) is not { } pane) return;
                    if (webView.CoreWebView2.ContainsFullScreenElement)
                        EnlargePane(pane);
                    else if (_enlargedPane == pane)
                        RestorePaneSize();
                };

                // 動画DL 用（#304・試験機能）：GraphQL レスポンスを傍受し、progressive MP4 の直 URL を
                // statusId 毎に保持する。JS からの直 fetch は CORS 不可のため、ここで拾って DL 時に使う。
                webView.CoreWebView2.WebResourceResponseReceived += (s, args) =>
                {
                    if (!_appSettings.VideoFrameSaveEnabled) return;
                    if (args.Request.Uri.IndexOf("/graphql/", StringComparison.OrdinalIgnoreCase) < 0) return;
                    CaptureVideoVariantsAsync(args).FireAndForget(nameof(CaptureVideoVariantsAsync));
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

            // ここから先も初期化の一部だが、従来は上の try/catch の範囲外だった。
            // fire-and-forget（_ = InitWebViewAsync(...)）で呼ばれるため、例外が発生しても
            // 誰も観測できず無言で失敗していた (#339)。メソッド全体を保護する。
            try
            {



                // キーボードショートカット：ブラウザ既定アクセラレータを無効化し JS で代替処理
                webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(KeyboardShortcutScript);
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(TimestampInterceptScript);
                // 編集状態レポーター（#258）：全ペインに注入し、編集中（リプライ/引用）を C# へ通知する。
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(EditStateReporterScript);
                // メディア拡大ボタン（#293）：全ペインに注入。config を先に入れてから本体を注入する。
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(BuildMediaOverlayButtonConfigJs());
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(MediaOverlayButtonScript);
                // ［…］メニューに「直前のリポストを検索」（#315）：全ペインに注入。config を先に入れてから本体を注入する。
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(BuildPriorRepostConfigJs());
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(PriorRepostSearchScript);
                // ホーム自動更新（#207）。ホームペインにのみ注入し、設定で ON/OFF・間隔を制御する。
                if (IsHomeConfig(cfg))
                {
                    await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(BuildHomeAutoLoadConfigJs());
                    await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(HomeAutoLoadScript);
                }
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
            catch (Exception ex)
            {
                LogError($"InitWebViewAsync/post-init (url={cfg.Url})", ex);
            }
        }
    }
}
