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
