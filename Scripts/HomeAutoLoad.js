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
