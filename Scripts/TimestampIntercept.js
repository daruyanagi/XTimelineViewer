(function() {
    if (window._xtvTimestamp) return;
    window._xtvTimestamp = true;
    document.addEventListener('click', function(e) {
        if (!window._xtvOpenTimestampInBrowser) return;
        var a = e.target.closest('a[href]');
        if (!a || !a.querySelector('time')) return;
        try {
            var url = new URL(a.href);
            if (/\/status\/\d+/.test(url.pathname)) {
                e.preventDefault();
                e.stopImmediatePropagation();
                window.chrome.webview.postMessage('openTimestamp:' + url.href);
            }
        } catch(ex) {}
    }, true);
})();
