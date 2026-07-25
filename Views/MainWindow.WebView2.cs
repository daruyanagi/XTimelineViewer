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

                var SEL = '[data-testid="tweetPhoto"], [data-testid="videoPlayer"]';

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
                        '[data-testid="tweet"]:hover .xtv-enlarge-btn{opacity:1!important;background:rgba(0,0,0,0.8);}' +
                        // 全画面中（動画は videoPlayer 自身が全画面）は自前の ⛶ を隠す。✕ と重なるため（#297）。
                        ':fullscreen .xtv-enlarge-btn{display:none!important;}' +
                        '.xtv-fs-close{position:fixed;top:16px;right:16px;z-index:2147483647;width:46px;height:46px;' +
                        'border:none;border-radius:8px;background:rgba(0,0,0,0.7);color:#fff;font-size:22px;cursor:pointer;}' +
                        // 自作機能ボタンは左上に置く（#304）: ダウンロード(left:16) / フレームキャプチャー(left:72)。
                        // X 固有の ✕ は右上のまま、という住み分けで迷いにくくする。カメラは動画のみ活性。
                        '.xtv-fs-btn{position:fixed;top:16px;z-index:2147483647;width:46px;height:46px;border:none;' +
                        'border-radius:8px;background:rgba(0,0,0,0.7);color:#fff;cursor:pointer;' +
                        'display:flex;align-items:center;justify-content:center;}' +
                        '.xtv-fs-dl{left:16px;}' +
                        '.xtv-fs-cam{left:72px;}' +
                        '.xtv-fs-btn:disabled{opacity:.35;cursor:default;}' +
                        '.xtv-toast{position:fixed;left:50%;bottom:44px;transform:translateX(-50%);z-index:2147483647;' +
                        'background:rgba(0,0,0,0.85);color:#fff;padding:10px 16px;border-radius:8px;font-size:14px;' +
                        'max-width:80vw;text-align:center;pointer-events:none;}' +
                        '.xtv-toast a{color:#8ec7ff;margin-left:12px;text-decoration:underline;cursor:pointer;pointer-events:auto;}' +
                        '.xtv-img-viewer{position:fixed;inset:0;width:100%;height:100%;background:#000;' +
                        'display:flex;align-items:center;justify-content:center;overflow:hidden;}' +
                        '.xtv-img-viewer img{max-width:100%;max-height:100%;width:auto;height:auto;object-fit:contain;}';
                    (document.head || document.documentElement).appendChild(s);
                }

                // コンテナが「写真」「動画」のどちらの拡大対象か判定する（#297）。
                // 動画は tweetPhoto の内側に videoPlayer として入れ子になっているため、素朴に tweetPhoto を
                // 対象にすると外側 tweetPhoto と内側 videoPlayer の両方にボタンが付き、画像ブランチが動画を
                // 画像として開いてしまう。動画は videoPlayer に一本化し、動画内包 tweetPhoto は除外する。
                //   ・写真: /media/ 画像を含み、動画要素を含まない tweetPhoto のみ
                //   ・動画: videoPlayer（controls を含む単位）
                function mediaKind(container) {
                    var t = container.getAttribute('data-testid');
                    if (t === 'tweetPhoto') {
                        if (container.querySelector('[data-testid="videoPlayer"], [data-testid="videoComponent"], video')) return null;
                        if (!container.querySelector('img[src*="pbs.twimg.com/media/"]')) return null;  // 画像未ロード → 後続 scan で拾う
                        return 'photo';
                    }
                    if (t === 'videoPlayer') return 'video';
                    return null;
                }

                function attach(container) {
                    if (!window._xtvMediaOverlayEnabled) return;
                    if (container.__xtvBtn) return;
                    var kind = mediaKind(container);
                    if (!kind) return;                 // 対象外（動画内包 tweetPhoto・画像未ロード等）
                    container.__xtvBtn = true;
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
                    // photo 判定時点で /media/ 画像の存在は保証されているが、念のため null ガード。
                    var srcImg = container.querySelector('img[src*="pbs.twimg.com/media/"]')
                              || container.querySelector('img');
                    if (!srcImg) return;
                    var hi = hiResUrl(srcImg.currentSrc || srcImg.src);
                    var viewer = document.createElement('div');
                    viewer.className = 'xtv-img-viewer';
                    viewer._xtvRef = getTweetRef(container);  // ダウンロード時のファイル名用（#304）
                    viewer._xtvImgUrl = hi;
                    var big = document.createElement('img');
                    big.src = hi;
                    viewer.appendChild(big);
                    document.body.appendChild(viewer);
                    // ✕・左上ボタンは fullscreenchange 側で付ける（動画/GIF と共通化）。
                    // requestFullscreen は非同期。失敗した場合だけビューアを片付ける。
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
                // folder（'pictures'|'videos'）を渡すと「フォルダーを開く」リンクを付ける（#308）。
                // link={label, message} を渡すとリンクを付ける（クリックで postMessage(message)）。
                function showToast(msg, link) {
                    var host = document.fullscreenElement || document.body;
                    if (!host) return;
                    var t = document.createElement('div');
                    t.className = 'xtv-toast';
                    t.textContent = msg;
                    if (link && link.label && link.message) {
                        var a = document.createElement('a');
                        a.className = 'xtv-toast-link';
                        a.href = '#';
                        a.textContent = link.label;
                        a.addEventListener('click', function (e) {
                            e.preventDefault(); e.stopPropagation();
                            try { window.chrome.webview.postMessage(link.message); } catch (x) {}
                        }, true);
                        t.appendChild(a);
                    }
                    host.appendChild(t);
                    setTimeout(function () { t.remove(); }, link ? 5000 : 2200);  // リンク付きは長めに
                }
                // 「フォルダーを開く」リンク付きトースト（#308）
                function toastFolder(msg, folder) {
                    showToast(msg, { label: (window._xtvFrameSaveL || {}).openFolder || 'Open folder', message: 'openFolder:' + folder });
                }
                // ダウンロード進捗トースト（#308）。自動では消さず、結果受信時に hideProgress で消す。
                function showProgress(text) {
                    var host = document.fullscreenElement || document.body;
                    if (!host) return;
                    var t = document.querySelector('.xtv-progress');
                    if (!t) { t = document.createElement('div'); t.className = 'xtv-toast xtv-progress'; host.appendChild(t); }
                    else if (t.parentNode !== host) { host.appendChild(t); }  // 全画面切替に追従
                    t.textContent = text;
                }
                function hideProgress() {
                    document.querySelectorAll('.xtv-progress').forEach(function (x) { x.remove(); });
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
                    showProgress((window._xtvFrameSaveL || {}).downloading || 'Downloading…');
                    // 形式: saveGif:<handle>|<status>|<url>（url は最後まで丸ごと。C# 側は Split 上限 3 で温存）
                    window.chrome.webview.postMessage('saveGif:' + r.handle + '|' + r.status + '|' + url);
                }
                // 画像ダウンロード（#304）。DL は全て C# 側で実行（JS fetch は CORS 不可）。
                function sendSaveImg(ref, url) {
                    if (!url) { showToast((window._xtvFrameSaveL || {}).failed || 'Failed'); return; }
                    showProgress((window._xtvFrameSaveL || {}).downloading || 'Downloading…');
                    window.chrome.webview.postMessage('saveImg:' + (ref.handle || '') + '|' + (ref.status || '') + '|' + url);
                }
                // 動画ダウンロード（#304）。progressive MP4 の直 URL は C# が GraphQL 傍受で保持。statusId で引く。
                function sendVideoDownload(video) {
                    var r = getTweetRef(video);
                    showProgress((window._xtvFrameSaveL || {}).downloading || 'Downloading…');
                    window.chrome.webview.postMessage('saveVideo:' + r.handle + '|' + r.status);
                }

                // ── オーバーレイのボタン生成（#304）──
                var XTV_CAMERA = '<svg viewBox="0 0 24 24" width="22" height="22" fill="#fff"><path d="M20 5h-3.17L15 3H9L7.17 5H4a2 2 0 00-2 2v12a2 2 0 002 2h16a2 2 0 002-2V7a2 2 0 00-2-2zm-8 13a5 5 0 110-10 5 5 0 010 10zm0-8a3 3 0 100 6 3 3 0 000-6z"></path></svg>';
                var XTV_DOWNLOAD = '<svg viewBox="0 0 24 24" width="22" height="22" fill="#fff"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"></path></svg>';
                function mkBtn(cls, svg, title) {
                    var b = document.createElement('button');
                    b.className = cls; b.type = 'button'; b.title = title || ''; b.innerHTML = svg;
                    return b;
                }
                function addCloseButton(host) {  // X 固有の閉じる（✕）は右上
                    if (host.querySelector(':scope > .xtv-fs-close')) return;
                    var c = document.createElement('button');
                    c.className = 'xtv-fs-close'; c.type = 'button'; c.textContent = '✕';
                    c.addEventListener('click', function (e) {
                        e.preventDefault(); e.stopPropagation();
                        try { if (document.exitFullscreen) document.exitFullscreen(); } catch (x) {}
                    }, true);
                    host.appendChild(c);
                }
                // 左上に［ダウンロード］［フレームキャプチャー］を付ける（#304）。
                // ダウンロードは全 kind で活性。カメラは kind==='video' のみ活性（画像/GIF は不活性）。
                function addLeftControls(host, kind, opts) {
                    if (host.querySelector(':scope > .xtv-fs-dl')) return;
                    var L = window._xtvFrameSaveL || {};
                    var dl = mkBtn('xtv-fs-btn xtv-fs-dl', XTV_DOWNLOAD, L.dlTip || 'Download');
                    dl.addEventListener('click', function (e) {
                        e.preventDefault(); e.stopPropagation();
                        if (kind === 'image') sendSaveImg(opts.ref || {}, opts.imgUrl);
                        else if (kind === 'gif') downloadGif(opts.video);
                        else sendVideoDownload(opts.video);
                    }, true);
                    host.appendChild(dl);
                    var cam = mkBtn('xtv-fs-btn xtv-fs-cam', XTV_CAMERA, L.tip || 'Save frame');
                    if (kind !== 'video') {
                        cam.disabled = true;
                    } else {
                        cam.addEventListener('click', function (e) {
                            e.preventDefault(); e.stopPropagation();
                            if (!captureFrame(opts.video)) showToast(L.failed || 'Failed');
                        }, true);
                    }
                    host.appendChild(cam);
                }
                // C# 側の保存結果を受けてトーストを出す。
                try {
                    window.chrome.webview.addEventListener('message', function (e) {
                        var d = e.data;
                        if (typeof d !== 'string') return;
                        var L = window._xtvFrameSaveL || {};
                        if (d.indexOf('dlProgress:') === 0) { showProgress((L.downloading || 'Downloading…') + ' ' + d.slice(11) + '%'); return; }
                        hideProgress();
                        if (d.indexOf('frameSaved:') === 0) toastFolder(L.saved || 'Saved', 'pictures');
                        else if (d.indexOf('gifSaved:') === 0) toastFolder(L.gifSaved || 'Saved', 'videos');
                        else if (d.indexOf('imgSaved:') === 0) toastFolder(L.imgSaved || 'Saved', 'pictures');
                        else if (d.indexOf('videoSaved:') === 0) toastFolder(L.videoSaved || 'Saved', 'videos');
                        // 動画DL 失敗時は回避策（ブログ）へのリンクを付ける（#310 のワークアラウンド）
                        else if (d === 'videoUnavailable') showToast(L.videoUnavailable || 'Failed', { label: L.help || 'How to fix', message: 'openHelp' });
                        else if (d === 'frameError') showToast(L.failed || 'Failed');
                    });
                } catch (x) {}

                // 全画面時にオーバーレイ操作を付ける（画像ビューア・動画・GIF 共通）。
                //   ✕（右上・常時） / 試験機能 ON のとき 左上に［DL］［カメラ］（#304）。
                function onFsChange() {
                    var fsEl = document.fullscreenElement;
                    if (fsEl) {
                        addCloseButton(fsEl);  // X 固有の閉じるは右上・常時
                        if (window._xtvFrameSaveEnabled) {
                            if (fsEl.classList.contains('xtv-img-viewer')) {
                                addLeftControls(fsEl, 'image', { ref: fsEl._xtvRef, imgUrl: fsEl._xtvImgUrl });
                            } else {
                                var video = fsEl.querySelector('video');
                                if (video) addLeftControls(fsEl, isGif(video) ? 'gif' : 'video', { video: video });
                            }
                        }
                    } else {
                        // 全画面終了: 画像ビューアと、付けた ✕・左上ボタン・トーストを後始末する。
                        var v = document.querySelector('.xtv-img-viewer');
                        if (v) v.remove();
                        document.querySelectorAll('.xtv-fs-close, .xtv-fs-btn, .xtv-toast')
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
            catch { }
        }

        // ポストのタイムスタンプ隣に「直前のポスト・リポストを検索」ボタン（🔎）を添える（試験機能 #315/#319）。
        // ［…］メニューへの注入はフォロー解除/ブロック等と隣接して誤爆しやすく、メニュー DOM 依存で
        // 壊れやすいので、タイムスタンプの直後に自前ボタンを置く方式にした。ホバーで薄く現れる。
        // クリックで C# へ searchPriorRepost:<handle>|<T(unix秒)> を送る。
        private static readonly string PriorRepostSearchScript = """
            (function () {
                if (window._xtvPriorRepost) return;
                window._xtvPriorRepost = true;

                function addStyle() {
                    if (document.getElementById('xtv-prior-style')) return;
                    var s = document.createElement('style');
                    s.id = 'xtv-prior-style';
                    s.textContent =
                        '.xtv-prior-btn{display:inline-flex;align-items:center;justify-content:center;gap:1px;vertical-align:middle;' +
                        'margin-left:6px;height:20px;padding:0 2px;border:none;background:transparent;' +
                        'color:rgb(83,100,113);cursor:pointer;opacity:0;transition:opacity .15s;}' +
                        '[data-testid="tweet"]:hover .xtv-prior-btn{opacity:.65;}' +
                        '.xtv-prior-btn:hover{opacity:1!important;color:#1d9bf0;}';
                    (document.head || document.documentElement).appendChild(s);
                }

                function attach(tw) {
                    if (!window._xtvPriorRepostEnabled) return;
                    if (tw.__xtvPriorBtn) return;
                    var timeA = tw.querySelector('a[href*="/status/"] time');
                    var a = timeA ? timeA.closest('a') : null;
                    if (!a) return;
                    var m = (a.getAttribute('href') || '').match(/^\/([^\/]+)\/status\/(\d+)/);
                    var iso = timeA.getAttribute('datetime');
                    if (!m || !iso) return;
                    tw.__xtvPriorBtn = true;
                    var handle = m[1];
                    var btn = document.createElement('button');
                    btn.className = 'xtv-prior-btn';
                    btn.type = 'button';
                    btn.title = window._xtvPriorRepostLabel || 'Search the preceding repost';
                    btn.setAttribute('aria-label', btn.title);
                    // 虫メガネ 1 つ。ポストとリポストを混ぜて時系列で遡るため（#319）
                    btn.innerHTML = '<svg viewBox="0 0 24 24" width="14" height="14" fill="currentColor"><path d="M10.25 3.75c-3.59 0-6.5 2.91-6.5 6.5s2.91 6.5 6.5 6.5c1.795 0 3.419-.726 4.596-1.904 1.178-1.177 1.904-2.801 1.904-4.596 0-3.59-2.91-6.5-6.5-6.5zm-8.5 6.5c0-4.694 3.806-8.5 8.5-8.5s8.5 3.806 8.5 8.5c0 1.986-.682 3.815-1.824 5.262l4.781 4.781-1.414 1.414-4.781-4.781c-1.447 1.142-3.276 1.824-5.262 1.824-4.694 0-8.5-3.806-8.5-8.5z"></path></svg>';
                    btn.addEventListener('click', function (e) {
                        e.preventDefault(); e.stopPropagation();
                        var t = Math.floor(new Date(iso).getTime() / 1000);
                        try { window.chrome.webview.postMessage('searchPriorRepost:' + handle + '|' + t); } catch (x) {}
                    }, true);
                    a.parentElement.appendChild(btn);  // タイムスタンプの直後（同じ親）に置く
                }

                function scan() {
                    if (!window._xtvPriorRepostEnabled) return;
                    var tweets = document.querySelectorAll('article[data-testid="tweet"]');
                    for (var i = 0; i < tweets.length; i++) attach(tweets[i]);
                }
                window._xtvPriorScan = scan;  // C# から ON 切り替え時に既存ポストへ付与するため

                var obs = new MutationObserver(function () { scan(); });
                function start() {
                    addStyle();
                    obs.observe(document.body || document.documentElement, { childList: true, subtree: true });
                    scan();
                }
                document.readyState === 'loading' ? document.addEventListener('DOMContentLoaded', start) : start();
            })();
            """;

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

        // 一時診断用（動画DL の GraphQL 傍受調査）。1 行だけ error.log に追記する。
        private static void LogDebug(string msg)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                File.AppendAllText(LogFilePath, $"[{DateTime.Now:HH:mm:ss}] DBG {msg}\n");
            }
            catch { }
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

                // 動画DL 用（#304・試験機能）：GraphQL レスポンスを傍受し、progressive MP4 の直 URL を
                // statusId 毎に保持する。JS からの直 fetch は CORS 不可のため、ここで拾って DL 時に使う。
                webView.CoreWebView2.WebResourceResponseReceived += (s, args) =>
                {
                    if (!_appSettings.VideoFrameSaveEnabled) return;
                    if (args.Request.Uri.IndexOf("/graphql/", StringComparison.OrdinalIgnoreCase) < 0) return;
                    _ = CaptureVideoVariantsAsync(args);
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
    }
}
