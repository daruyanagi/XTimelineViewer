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

        // 編集状態レポーター（#258）。全ペインに注入し、編集中（モーダル表示／編集要素フォーカス）に
        // なったら postMessage('editing:true'/'editing:false') で C# に通知する。C# 側は「いずれかの
        // ペインが編集中」を集約してホーム自動更新を一時停止する（別ペインの下書き消失を防ぐ）。
        private static readonly string EditStateReporterScript = """
            (function () {
                if (window._xtvEditWatch) return;
                window._xtvEditWatch = true;
                var last = null;
                function isEditing() {
                    if (document.querySelector('[aria-modal="true"]')) return true;
                    var fe = document.activeElement;
                    return !!(fe && (fe.isContentEditable || fe.tagName === 'TEXTAREA' || fe.tagName === 'INPUT'));
                }
                function report() {
                    var v = isEditing();
                    if (v === last) return;
                    last = v;
                    try { window.chrome.webview.postMessage('editing:' + v); } catch (e) {}
                }
                document.addEventListener('focusin', report, true);
                document.addEventListener('focusout', function () { setTimeout(report, 0); }, true);
                setInterval(report, 1000);  // モーダル開閉など focus 変化を伴わないケースのバックストップ
                report();
            })();
            """;

        // メディア拡大ボタンのオーバーレイ（試験機能 #293）。全ペインに注入し、タイムライン上の
        // 画像・動画コンテナに「⛶」ボタンを重ねる。押すとメディアを全画面表示し（＝メディアだけに
        // フォーカス）、WebView2 の ContainsFullScreenElement が立って既存の全画面フック（#291）で
        // ペインが画面いっぱいに拡大される。全画面中は「✕」ボタンを重ね、Esc とあわせて戻れる。
        // window._xtvMediaOverlayEnabled で ON/OFF を制御する。
        //   ・画像（#295）: 内部 <img> を専用ビューア div に入れて全画面化する。div は背景黒・
        //     object-fit:contain なので、コンテナごと全画面にしていた頃の上下見切れが起きない。
        //     さらに src の name=... を orig へ差し替えて拡大時だけ高解像度版を読み込む。
        //   ・動画: 従来どおりコンテナを全画面化してカスタムコントロールを保つ。
        private static readonly string MediaOverlayButtonScript = """
            (function () {
                if (window._xtvMediaBtn) return;
                window._xtvMediaBtn = true;

                var SEL = '[data-testid="tweetPhoto"], [data-testid="videoPlayer"], [data-testid="videoComponent"]';

                function addStyle() {
                    if (document.getElementById('xtv-media-btn-style')) return;
                    var s = document.createElement('style');
                    s.id = 'xtv-media-btn-style';
                    s.textContent =
                        // 既定は控えめ（半透明）、ホバーで濃く（#297）。実機ではプライマリメディアで
                        // ボタンの opacity が上書きされて消える事象があったため、opacity は !important で
                        // 固定し、z-index もほぼ最大に上げて被りにも耐える。暗い画像でも縁が分かるよう
                        // 薄い白リング＋影を添える。
                        // 左上に置く（#297）。狭いペインで画像の右側が見切れると right:8px 固定のボタンも
                        // 画面外に出て見えなくなるため。左端は常に見えるので left:8px にする。
                        '.xtv-enlarge-btn{position:absolute!important;top:8px;left:8px;z-index:2147483000;width:34px;height:34px;' +
                        'border:none;border-radius:6px;background:rgba(0,0,0,0.55);color:#fff;font-size:16px;' +
                        'cursor:pointer;display:flex!important;align-items:center;justify-content:center;opacity:.55!important;' +
                        'box-shadow:0 0 0 1px rgba(255,255,255,0.35),0 1px 3px rgba(0,0,0,0.5);transition:opacity .15s,background .15s;}' +
                        '.xtv-enlarge-host:hover .xtv-enlarge-btn,[data-testid="tweet"]:hover .xtv-enlarge-btn{opacity:1!important;background:rgba(0,0,0,0.8);}' +
                        // 全画面中（動画は videoPlayer 自身が全画面）は自前の ⛶ を隠す。✕ と重なるため（#297）。
                        ':fullscreen .xtv-enlarge-btn{display:none!important;}' +
                        '.xtv-fs-close{position:fixed;top:16px;right:16px;z-index:2147483647;width:46px;height:46px;' +
                        'border:none;border-radius:8px;background:rgba(0,0,0,0.7);color:#fff;font-size:22px;cursor:pointer;}' +
                        // 動画フレーム保存ボタン（#299）：全画面中に ✕ の左へ置く。
                        '.xtv-fs-save{position:fixed;top:16px;right:72px;z-index:2147483647;width:46px;height:46px;' +
                        'border:none;border-radius:8px;background:rgba(0,0,0,0.7);color:#fff;cursor:pointer;' +
                        'display:flex;align-items:center;justify-content:center;}' +
                        '.xtv-toast{position:fixed;left:50%;bottom:44px;transform:translateX(-50%);z-index:2147483647;' +
                        'background:rgba(0,0,0,0.85);color:#fff;padding:10px 16px;border-radius:8px;font-size:14px;' +
                        'max-width:80vw;text-align:center;pointer-events:none;}' +
                        '.xtv-img-viewer{position:fixed;inset:0;width:100%;height:100%;background:#000;' +
                        'display:flex;align-items:center;justify-content:center;overflow:hidden;}' +
                        '.xtv-img-viewer img{max-width:100%;max-height:100%;width:auto;height:auto;object-fit:contain;}';
                    (document.head || document.documentElement).appendChild(s);
                }

                // コンテナが「写真」「動画」のどちらの拡大対象か判定する（#297）。
                // 動画は tweetPhoto の内側に videoPlayer/videoComponent として入れ子になっているため、
                // 素朴に SEL でマッチすると外側 tweetPhoto と内側 player の両方にボタンが付き、
                // 画像ブランチが動画を画像として開いてしまう。ここで一意な単位へ正規化する。
                //   ・写真: /media/ 画像を含み、動画要素を含まない tweetPhoto のみ
                //   ・動画: videoPlayer（controls を含む単位）。配下の videoComponent は重複なので除外
                function mediaKind(container) {
                    var t = container.getAttribute('data-testid');
                    if (t === 'tweetPhoto') {
                        if (container.querySelector('[data-testid="videoPlayer"], [data-testid="videoComponent"], video')) return null;
                        if (!container.querySelector('img[src*="pbs.twimg.com/media/"]')) return null;  // 画像未ロード → 後続 scan で拾う
                        return 'photo';
                    }
                    if (t === 'videoPlayer') return 'video';
                    if (t === 'videoComponent') {
                        var vp = container.closest('[data-testid="videoPlayer"]');
                        return (vp && vp !== container) ? null : 'video';  // videoPlayer 配下は重複
                    }
                    return null;
                }

                function attach(container) {
                    if (!window._xtvMediaOverlayEnabled) return;
                    if (container.__xtvBtn) return;
                    var kind = mediaKind(container);
                    if (!kind) return;                 // 対象外（動画内包 tweetPhoto・入れ子 videoComponent・画像未ロード等）
                    container.__xtvBtn = true;
                    container.classList.add('xtv-enlarge-host');
                    if (getComputedStyle(container).position === 'static') container.style.position = 'relative';
                    var btn = document.createElement('button');
                    btn.className = 'xtv-enlarge-btn';
                    btn.type = 'button';
                    btn.textContent = '⛶';
                    btn.addEventListener('click', function (e) {
                        e.preventDefault(); e.stopPropagation();
                        if (kind === 'photo') openImageViewer(container);
                        else { try { if (container.requestFullscreen) container.requestFullscreen(); } catch (x) {} }
                    }, true);
                    container.appendChild(btn);
                }

                // pbs.twimg.com の画像 URL を最大解像度（name=orig）へ変換する（#295）。
                function hiResUrl(src) {
                    try {
                        var u = new URL(src, location.href);
                        if (u.hostname.indexOf('pbs.twimg.com') === -1) return src;
                        u.searchParams.set('name', 'orig');
                        if (!u.searchParams.has('format')) u.searchParams.set('format', 'jpg');
                        return u.toString();
                    } catch (e) { return src; }
                }

                // 画像を専用ビューア div（背景黒・contain・高解像度）で全画面表示する（#295）。
                function openImageViewer(container) {
                    var srcImg = container.querySelector('img[src*="pbs.twimg.com/media/"]')
                              || container.querySelector('img');
                    if (!srcImg) {
                        try { if (container.requestFullscreen) container.requestFullscreen(); } catch (x) {}
                        return;
                    }
                    var viewer = document.createElement('div');
                    viewer.className = 'xtv-img-viewer';
                    var big = document.createElement('img');
                    big.src = hiResUrl(srcImg.currentSrc || srcImg.src);
                    viewer.appendChild(big);
                    var c = document.createElement('button');
                    c.className = 'xtv-fs-close';
                    c.type = 'button';
                    c.textContent = '✕';
                    c.addEventListener('click', function (e) {
                        e.preventDefault(); e.stopPropagation();
                        try { if (document.fullscreenElement) document.exitFullscreen(); } catch (x) {}
                    }, true);
                    viewer.appendChild(c);
                    document.body.appendChild(viewer);
                    // requestFullscreen は非同期。失敗した場合だけビューアを片付ける
                    // （成功時の後始末は fullscreenchange 側で行う）。
                    try {
                        var p = viewer.requestFullscreen && viewer.requestFullscreen();
                        if (p && p.catch) p.catch(function () { viewer.remove(); });
                    } catch (x) { viewer.remove(); }
                }

                function scan() {
                    if (!window._xtvMediaOverlayEnabled) return;
                    var list = document.querySelectorAll(SEL);
                    for (var i = 0; i < list.length; i++) attach(list[i]);
                }
                window._xtvMediaBtnRescan = scan;  // C# から ON 切り替え時に既存メディアへ付与するため

                // ── 動画フレーム保存（#299）──
                // 全画面中の <video> の現在フレームを canvas に焼き、base64 PNG を C# に送って保存する。
                // 全画面中は全画面要素の子孫しか描画されないため、トーストは全画面要素側へ挿入する。
                function showToast(msg) {
                    var host = document.fullscreenElement || document.body;
                    if (!host) return;
                    var t = document.createElement('div');
                    t.className = 'xtv-toast';
                    t.textContent = msg;
                    host.appendChild(t);
                    setTimeout(function () { t.remove(); }, 2200);
                }
                // 動画が属するツイートの handle / status ID を求める（ファイル名でソースを辿れるように）。
                // 全画面中でも article は DOM に残るので closest で辿れる。取得失敗時は空を返す。
                function getTweetRef(el) {
                    try {
                        var tw = el.closest('[data-testid="tweet"]');
                        if (!tw) return { handle: '', status: '' };
                        var timeA = tw.querySelector('a[href*="/status/"] time');
                        var a = timeA ? timeA.closest('a') : tw.querySelector('a[href*="/status/"]');
                        var m = a && (a.getAttribute('href') || '').match(/^\/([^\/]+)\/status\/(\d+)/);
                        return m ? { handle: m[1], status: m[2] } : { handle: '', status: '' };
                    } catch (e) { return { handle: '', status: '' }; }
                }
                function captureFrame(video) {
                    if (!video || !video.videoWidth || !video.videoHeight) return false;
                    try {
                        var c = document.createElement('canvas');
                        c.width = video.videoWidth; c.height = video.videoHeight;
                        c.getContext('2d').drawImage(video, 0, 0, c.width, c.height);
                        var url = c.toDataURL('image/png');
                        var r = getTweetRef(video);
                        // 形式: saveFrame:<handle>|<status>|<base64>（handle/status は英数と _ のみで | を含まない）
                        window.chrome.webview.postMessage('saveFrame:' + r.handle + '|' + r.status + '|' + url.slice(url.indexOf(',') + 1));
                        return true;
                    } catch (e) { return false; }
                }
                // X の「GIF」は無音ループ MP4。<video> の src が blob でなく tweet_video 直リンクなら GIF（#301）。
                // GIF は別オリジン直リンクのため canvas が汚染されフレーム保存は失敗する → 代わりに mp4 を落とす。
                function isGif(video) {
                    var s = (video && (video.currentSrc || video.src)) || '';
                    return s.indexOf('blob:') !== 0 && /tweet_video/.test(s);
                }
                function downloadGif(video) {
                    var url = (video && (video.currentSrc || video.src)) || '';
                    if (!/^https?:/.test(url)) { showToast((window._xtvFrameSaveL || {}).failed || 'Failed'); return; }
                    var r = getTweetRef(video);
                    // 形式: saveGif:<handle>|<status>|<url>（url は最後まで丸ごと。C# 側は Split 上限 3 で温存）
                    window.chrome.webview.postMessage('saveGif:' + r.handle + '|' + r.status + '|' + url);
                }
                // C# 側の保存結果を受けてトーストを出す。
                try {
                    window.chrome.webview.addEventListener('message', function (e) {
                        var d = e.data;
                        if (typeof d !== 'string') return;
                        var L = window._xtvFrameSaveL || {};
                        if (d.indexOf('frameSaved:') === 0) showToast(L.saved || 'Saved');
                        else if (d.indexOf('gifSaved:') === 0) showToast(L.gifSaved || 'Saved');
                        else if (d === 'frameError') showToast(L.failed || 'Failed');
                    });
                } catch (x) {}

                // 全画面中は「✕」ボタンを全画面要素に重ねる（Esc でも解除できる）。
                // 画像ビューア（.xtv-img-viewer）は自前で ✕ を内包するのでここでは付けない。
                function onFsChange() {
                    var fsEl = document.fullscreenElement;
                    if (fsEl && !fsEl.classList.contains('xtv-img-viewer')) {
                        if (!fsEl.querySelector(':scope > .xtv-fs-close')) {
                            var c = document.createElement('button');
                            c.className = 'xtv-fs-close';
                            c.type = 'button';
                            c.textContent = '✕';
                            c.addEventListener('click', function (e) {
                                e.preventDefault(); e.stopPropagation();
                                try { if (document.exitFullscreen) document.exitFullscreen(); } catch (x) {}
                            }, true);
                            fsEl.appendChild(c);
                        }
                        // 動画かつ試験機能 ON なら ✕ の左にボタンを置く（#299/#301）。
                        // 通常動画: カメラ＝フレーム保存。GIF: 汚染で保存できないため代わりに mp4 ダウンロード。
                        if (window._xtvFrameSaveEnabled && fsEl.querySelector('video') && !fsEl.querySelector(':scope > .xtv-fs-save')) {
                            var L = window._xtvFrameSaveL || {};
                            var gif = isGif(fsEl.querySelector('video'));
                            var CAMERA = '<svg viewBox="0 0 24 24" width="22" height="22" fill="#fff"><path d="M20 5h-3.17L15 3H9L7.17 5H4a2 2 0 00-2 2v12a2 2 0 002 2h16a2 2 0 002-2V7a2 2 0 00-2-2zm-8 13a5 5 0 110-10 5 5 0 010 10zm0-8a3 3 0 100 6 3 3 0 000-6z"></path></svg>';
                            var DOWNLOAD = '<svg viewBox="0 0 24 24" width="22" height="22" fill="#fff"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"></path></svg>';
                            var sv = document.createElement('button');
                            sv.className = 'xtv-fs-save';
                            sv.type = 'button';
                            sv.title = gif ? (L.gifTip || 'Save GIF') : (L.tip || 'Save frame');
                            sv.innerHTML = gif ? DOWNLOAD : CAMERA;
                            sv.addEventListener('click', function (e) {
                                e.preventDefault(); e.stopPropagation();
                                var video = fsEl.querySelector('video');
                                if (gif) downloadGif(video);
                                else if (!captureFrame(video)) showToast((window._xtvFrameSaveL || {}).failed || 'Failed');
                            }, true);
                            fsEl.appendChild(sv);
                        }
                    } else if (!fsEl) {
                        // 全画面終了: 画像ビューアと、動画コンテナに付けた ✕・保存ボタン・トーストを後始末する。
                        var v = document.querySelector('.xtv-img-viewer');
                        if (v) v.remove();
                        document.querySelectorAll('.xtv-fs-close, .xtv-fs-save, .xtv-toast')
                               .forEach(function (x) { x.remove(); });
                    }
                }
                document.addEventListener('fullscreenchange', onFsChange, true);

                var obs = new MutationObserver(function () { scan(); });
                function start() {
                    addStyle();
                    obs.observe(document.body || document.documentElement, { childList: true, subtree: true });
                    scan();
                }
                document.readyState === 'loading' ? document.addEventListener('DOMContentLoaded', start) : start();
            })();
            """;

        /// <summary>現在の設定を、メディア拡大ボタンの JS 制御変数へ反映するスニペット（#293）。
        /// フレーム保存（#299）のローカライズ文言もここで JS へ渡す。</summary>
        private string BuildMediaOverlayButtonConfigJs()
        {
            var labels = System.Text.Json.JsonSerializer.Serialize(new
            {
                tip      = R.Get("MediaFrameSave_Tooltip"),
                saved    = R.Get("MediaFrameSave_Saved"),
                failed   = R.Get("MediaFrameSave_Failed"),
                gifTip   = R.Get("MediaFrameSave_GifTooltip"),
                gifSaved = R.Get("MediaFrameSave_GifSaved"),
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
            catch { }
        }

        /// <summary>cfg がホームタイムラインかどうか。</summary>
        private static bool IsHomeConfig(TimelineConfig cfg)
            => Uri.TryCreate(cfg.Url, UriKind.Absolute, out var u)
               && u.AbsolutePath.StartsWith("/home", StringComparison.OrdinalIgnoreCase);

        // ホームタイムライン自動更新（#207）。同梱拡張 TwitterTimelineLoader（TLLoader_main.js）の
        // ロジックをできるだけ忠実に移植。/home でページ先頭にいるとき一定間隔で
        // ホームタブ（a[data-testid="AppTabBar_Home_Link"]）を click して新着を取り込む。
        // 変更点: chrome.storage 依存を撤去し、window._xtvHomeAutoLoadEnabled / _xtvHomeAutoLoadIntervalMs で制御。
        //         状態（稼働中/一時停止/オフ）を postMessage('homeAutoLoad:...') でアプリに通知し、
        //         ヘッダーのインジケーターへ反映する。参考: https://qiita.com/ryounagaoka/items/a48d3a4c4faf78a99ae5
        private static readonly string HomeAutoLoadScript = """
            (function () {
                if (window._xtvTtlInit) return;
                window._xtvTtlInit = true;

                var g_ttlTopCount = 0;
                var g_ttlTimerInterval = 1000;
                var lastStatus = '';

                function intervalMs() {
                    var v = window._xtvHomeAutoLoadIntervalMs;
                    return (typeof v === 'number' && v >= 1000) ? v : 8000;
                }

                function report(status) {
                    if (status === lastStatus) return;
                    lastStatus = status;
                    try { window.chrome.webview.postMessage('homeAutoLoad:' + status); } catch (e) {}
                }

                function isHome() {
                    return window.location.href == "https://twitter.com/home"
                        || window.location.href == "https://x.com/home";
                }

                // 更新を控えるべき理由（下書き消失・誤操作の防止）。なければ null。
                function suppressReason() {
                    var searchCandidate = document.body.querySelectorAll('div[class="css-1dbjc4n r-13awgt0 r-bnwqim"]');
                    if (searchCandidate.length > 0 && searchCandidate[0].innerHTML != "") return 'search';
                    // 返信/引用/投稿などのモーダルが開いている間は下書き消失防止のため止める
                    if (document.querySelector('[aria-modal="true"]')) return 'input';
                    // 実際に編集可能な要素にフォーカスがあるときだけ止める（#232）。
                    // リポスト/いいね等のボタンにフォーカスが移っても止めないようにする。
                    var fe = document.activeElement;
                    if (fe && (fe.isContentEditable || fe.tagName === 'TEXTAREA' || fe.tagName === 'INPUT')) return 'input';
                    return null;
                }

                function tick() {
                    if (!window._xtvHomeAutoLoadEnabled) { report('off'); return; }
                    if (!isHome()) { report('idle'); return; }
                    if (window.pageYOffset > 5.0) { g_ttlTopCount = intervalMs(); report('paused-scroll'); return; }
                    var reason = suppressReason();
                    if (reason) { report('paused-' + reason); return; }
                    if (window._xtvAnyComposing) { report('paused-elsewhere'); return; }  // 他ペインで編集中（#258）
                    report('running');
                    if (g_ttlTopCount >= intervalMs()) {
                        var homeButton = document.body.querySelectorAll('a[data-testid="AppTabBar_Home_Link"]');
                        if (homeButton.length > 0) homeButton[0].click();
                        g_ttlTopCount = 0;
                    }
                    g_ttlTopCount += g_ttlTimerInterval;
                }

                setInterval(tick, g_ttlTimerInterval);
            })();
            """;

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
            catch { }
        }

        /// <summary>いずれかのペインが編集中かを全ペインの JS（window._xtvAnyComposing）へ反映する（#258）。</summary>
        private void UpdateAnyComposing()
        {
            var any = _composingWebViews.Count > 0 ? "true" : "false";
            foreach (var wv in _webViews)
                if (wv.CoreWebView2 is not null)
                    _ = wv.CoreWebView2.ExecuteScriptAsync($"window._xtvAnyComposing = {any};");
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
                    }
                    EvaluateHardReloadPause(webView);

                    // 画像表示中はペインを一時拡大する（試験機能 #287）
                    if (_appSettings.MediaEnlargeEnabled &&
                        _webViewToPane.TryGetValue(webView, out var pane))
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
                    if (!_webViewToPane.TryGetValue(webView, out var pane)) return;
                    if (webView.CoreWebView2.ContainsFullScreenElement)
                        EnlargePane(pane);
                    else if (_enlargedPane == pane)
                        RestorePaneSize();
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
            // 編集状態レポーター（#258）：全ペインに注入し、編集中（リプライ/引用）を C# へ通知する。
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(EditStateReporterScript);
            // メディア拡大ボタン（#293）：全ペインに注入。config を先に入れてから本体を注入する。
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(BuildMediaOverlayButtonConfigJs());
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(MediaOverlayButtonScript);
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
    }
}
