
function ttlLoadOptions() {
    chrome.storage.local.get("g_ttlIntervalSec", function(items) {
        var resultIntervalSec = items.g_ttlIntervalSec;
        if (typeof resultIntervalSec === "number" && isFinite(resultIntervalSec)) {
            if (resultIntervalSec < 5.0) {
                resultIntervalSec = 5.0;
            }
        }
        else {
            resultIntervalSec = 15.0;
        }
        document.getElementById("TTL_IntervalSec").value = resultIntervalSec.toString();
        chrome.storage.local.set({"g_ttlIntervalSec": resultIntervalSec}, function(){});
    });
}

function ttlSaveOptions() {
    var saveTtlIntervalSec = parseFloat(document.getElementById("TTL_IntervalSec").value);
    if (isNaN(saveTtlIntervalSec)) {
        document.getElementById("TTL_Errors").innerHTML
            = "更新時間に数値を設定してください。<br>Put number in update interval.<br>"
        return;
    }
    if (saveTtlIntervalSec < 5.0) {
        saveTtlIntervalSec = 5.0;
    }

    chrome.storage.local.set({"g_ttlIntervalSec": saveTtlIntervalSec}, function(){});
    window.close();
}

document.addEventListener("DOMContentLoaded", ttlLoadOptions);

document.getElementById("TTL_SaveOptions").addEventListener("click", ttlSaveOptions);
