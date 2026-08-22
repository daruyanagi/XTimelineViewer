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
