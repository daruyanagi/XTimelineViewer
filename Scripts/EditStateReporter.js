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
