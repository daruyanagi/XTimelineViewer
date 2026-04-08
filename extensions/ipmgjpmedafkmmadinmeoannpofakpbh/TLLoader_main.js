//参考資料
//https://qiita.com/ryounagaoka/items/a48d3a4c4faf78a99ae5

var g_ttlTopCount = 0;
var g_ttlIntervalMilliSec = 15000; //初期値はオリジナルの倍、15秒に設定。5秒未満にはできない
var g_ttlTimerInterval = 1000;

function loadTtlOptions() {
    chrome.storage.local.get("g_ttlIntervalSec", function(items) {
        var resultIntervalSec = items.g_ttlIntervalSec;
        if (typeof resultIntervalSec === "number" && isFinite(resultIntervalSec)) {
            if (resultIntervalSec < 5.0) {
                resultIntervalSec = 5.0;
            }
        }
        else {
            resultIntervalSec = 7.5;
        }
        g_ttlIntervalMilliSec = resultIntervalSec * 1000;
        chrome.storage.local.set({"g_ttlIntervalSec": resultIntervalSec}, function(){});
    });
}

function shouldNotUpdate() {
    var searchCandidate = document.body.querySelectorAll('div[class="css-1dbjc4n r-13awgt0 r-bnwqim"]');
    var focusElement = document.activeElement;
    if (searchCandidate.length > 0) {
        if (searchCandidate[0].innerHTML != "") {
            //更新してしまうと検索ボックスが閉じてしまうため、この場合は更新しない
            return true;
        }
    }

    if (focusElement.tagName != "BODY") {
        //ツイート入力などにフォーカスが当たっている場合には処理しないようにする（フォーカスが外れる）
        var focusAttr = focusElement.getAttribute("data-testid");
        if (focusAttr != "tweet" && focusAttr != "AppTabBar_Home_Link") {
            //ホームボタンを押すと、ツイートにフォーカスが設定されることがある。その場合は更新する。
            return true;
        }

    }
    return false;
}

function updateTimeline() {
    if (window.location.href == "https://twitter.com/home"
        || window.location.href == "https://x.com/home") {
        if (window.pageYOffset <= 5.0) {
            if (!shouldNotUpdate()) {
                if (g_ttlTopCount >= g_ttlIntervalMilliSec) {
                    var homeButton = document.body.querySelectorAll('a[data-testid="AppTabBar_Home_Link"]');
                    if (homeButton.length > 0) {
                        homeButton[0].click();
                    }
                    g_ttlTopCount = 0;
                    loadTtlOptions();
                }
                g_ttlTopCount += g_ttlTimerInterval;
            }
        }
        else {
            g_ttlTopCount = g_ttlIntervalMilliSec;
        }
    }
}

setInterval(function(){
    loadTtlOptions();
    updateTimeline();
}, g_ttlTimerInterval);
