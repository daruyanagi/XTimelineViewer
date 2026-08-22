(function() {
    if (window._xtvKb) return;
    window._xtvKb = true;

    function addStyle() {
        if (document.getElementById('xtv-kb-style')) return;
        var s = document.createElement('style');
        s.id = 'xtv-kb-style';
        s.textContent = '.xtv-focused-post{outline:2px solid #0078D4!important;outline-offset:-2px!important;border-radius:4px!important;}';
        (document.head || document.documentElement).appendChild(s);
    }
    document.readyState === 'loading'
        ? document.addEventListener('DOMContentLoaded', addStyle)
        : addStyle();

    var fi = -1;
    var getPosts = () => [...document.querySelectorAll('article[data-testid="tweet"]')];
    var isEdit   = () => { var el = document.activeElement; return el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable); };

    function navigatePosts(d) {
        var ps = getPosts();
        if (!ps.length) return;
        ps.forEach(a => a.classList.remove('xtv-focused-post'));
        fi = fi < 0 ? (d > 0 ? 0 : ps.length - 1)
                    : Math.max(0, Math.min(ps.length - 1, fi + d));
        ps[fi]?.classList.add('xtv-focused-post');
        ps[fi]?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    function actOnPost(id, alt) {
        var ps = getPosts();
        if (!ps.length) return;
        var idx = fi;
        if (idx < 0 || idx >= ps.length) {
            // タイムラインでは Ctrl+↑/↓ での選択が必要（案C）。
            // ただし個別ツイート（/status/）ページでは未選択でも主ツイート（先頭）に作用する（#254）。
            if (/\/status\/\d+/.test(location.pathname)) idx = 0;
            else return;
        }
        var b = ps[idx].querySelector('[data-testid="' + id + '"]' + (alt ? ',[data-testid="' + alt + '"]' : ''));
        b?.click();
    }

    document.addEventListener('keydown', e => {
        var c = e.ctrlKey, s = e.shiftKey, a = e.altKey, k = e.key, ni = !isEdit();
        if (c && !s && !a) {
            if (k === 'ArrowRight') { e.preventDefault(); window.chrome.webview.postMessage('focusNext'); return; }
            if (k === 'ArrowLeft')  { e.preventDefault(); window.chrome.webview.postMessage('focusPrev'); return; }
            if (k === 'n')          { e.preventDefault(); window.chrome.webview.postMessage('newPost');   return; }
            if (k === 'ArrowUp')    { e.preventDefault(); navigatePosts(-1); return; }
            if (k === 'ArrowDown')  { e.preventDefault(); navigatePosts(1);  return; }
            if (k === 'f')          { e.preventDefault(); window.chrome.webview.postMessage('focusSearch'); return; }
            if (k >= '1' && k <= '9') { e.preventDefault(); window.chrome.webview.postMessage('focusIndex:' + k); return; } // #225

            if (k === 'r' && ni)    { e.preventDefault(); actOnPost('retweet',  'unretweet');      return; }
            if (k === 'b' && ni)    { e.preventDefault(); actOnPost('bookmark', 'removeBookmark'); return; }
            if (k === 'l' && ni)    { e.preventDefault(); actOnPost('like',     'unlike');         return; }
        }
        // Ctrl+Shift+←/→ でペインを左右へ並べ替え（#344）。
        // 入力中は単語単位の選択を奪わないよう ni で除外する。
        if (c && s && !a && ni) {
            if (k === 'ArrowRight') { e.preventDefault(); window.chrome.webview.postMessage('movePaneNext'); return; }
            if (k === 'ArrowLeft')  { e.preventDefault(); window.chrome.webview.postMessage('movePanePrev'); return; }
        }
        if (!c && !s && !a) {
            if (k === 'F3')              { e.preventDefault(); window.chrome.webview.postMessage('focusSearch'); return; } // #228
            if (k === 'Home'      && ni) { window.scrollTo({ top: 0, behavior: 'smooth' }); return; }
            if (k === 'End'       && ni) { window.scrollTo({ top: document.documentElement.scrollHeight, behavior: 'smooth' }); return; }
            if (k === 'F5')              { e.preventDefault(); location.reload(); return; }
            if (k === 'Backspace' && ni) { e.preventDefault(); history.back(); return; }
        }
    }, true);

    // マウスホイールでスクロールしたら、そのペインをアクティブ化する (#221)。
    // ホイールはキーフォーカスを移さないため、Home/End 等が別ペインに効いてしまうのを防ぐ。
    // 既にフォーカスがある（hasFocus）ときは何もしない。連打防止に 200ms スロットル。
    var lastAct = 0;
    document.addEventListener('wheel', function () {
        var now = Date.now();
        if (now - lastAct < 200) return;
        lastAct = now;
        if (!document.hasFocus()) window.chrome.webview.postMessage('activate');
    }, { passive: true, capture: true });

    // Shift+ホイールでペインを横スクロールする（#371）。
    // ヘッダーや余白の上では WinUI の ScrollViewer が縦ホイールを
    // 自動で横へ回すが、ここは WebView2 なので届かない。
    //
    // 上のリスナーは passive なので preventDefault が効かない。別に登録する。
    // 非 passive はブラウザーのスクロール高速パスを外すので、
    // Shift が無いときは最初の 1 行で抜けること。
    document.addEventListener('wheel', function (e) {
        if (!e.shiftKey || e.ctrlKey || e.altKey) return;
        // X 側に横方向のオーバーフローがある画面で、
        // ページが横に動いてしまうのを防ぐ。
        e.preventDefault();
        var d = e.deltaY || e.deltaX;
        if (d) window.chrome.webview.postMessage('scrollPanes:' + d);
    }, { passive: false, capture: true });
})();
