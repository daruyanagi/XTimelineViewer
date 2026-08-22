using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.UI;

using XTimelineViewer.Models;

using XTimelineViewer.Views.Controls;

namespace XTimelineViewer.Views
{
    public sealed partial class MainWindow : Window
    {
        private void StartHardReloadTimer(WebView2 wv, TimelineConfig cfg)
        {
            StopHardReloadTimer(wv);
            if (!cfg.HardReloadEnabled) return;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(cfg.HardReloadInterval) };
            timer.Tick += (_, _) =>
            {
                wv.CoreWebView2?.Reload();
                _hardReloadStartTimes[wv] = DateTimeOffset.Now;
            };
            _hardReloadStartTimes[wv] = DateTimeOffset.Now;
            timer.Start();
            _hardReloadTimers[wv] = timer;
        }

        private void StopHardReloadTimer(WebView2 wv)
        {
            if (_hardReloadTimers.Remove(wv, out var t)) t.Stop();
            _hardReloadStartTimes.Remove(wv);
        }

        // WebView2 インスタンスに紐づくすべてのリソースを解放し、CoreWebView2 を閉じる。
        // タイムライン削除・プロファイル切り替え・ウィンドウクローズ時に必ず呼ぶこと。
        private void CleanupWebView(WebView2 wv)
        {
            var source = wv.CoreWebView2?.Source ?? "(not initialized)";
            Debug.WriteLine($"[WebView2] CleanupWebView: source={source}");

            StopHardReloadTimer(wv);
            _hardReloadUiUpdaters.Remove(wv);
            _pointerOverWebViews.Remove(wv);
            _urlDivergedWebViews.Remove(wv);
            _webViews.Remove(wv);
            _webViewToPane.Remove(wv);
            if (_composingWebViews.Remove(wv)) UpdateAnyComposing();  // #258: 編集中ペインが消えたら反映
            try
            {
                wv.Close();
                Debug.WriteLine($"[WebView2] Closed: source={source}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebView2] Close failed: source={source}, error={ex.Message}");
            }
        }

        private void EvaluateHardReloadPause(WebView2 wv)
        {
            if (!_hardReloadTimers.TryGetValue(wv, out var t)) return;
            bool shouldPause = _pointerOverWebViews.Contains(wv) || _urlDivergedWebViews.Contains(wv);
            if (shouldPause && t.IsEnabled)
                t.Stop();
            else if (!shouldPause && !t.IsEnabled)
            {
                _hardReloadStartTimes[wv] = DateTimeOffset.Now;
                t.Start();
            }
        }

        private string GetHardReloadTooltipText(WebView2 wv)
        {
            if (!_hardReloadTimers.TryGetValue(wv, out var t))
                return R.Get("HardReload_Disabled");
            if (!t.IsEnabled)
                return _urlDivergedWebViews.Contains(wv)
                    ? R.Get("HardReload_Paused_Nav")
                    : R.Get("HardReload_Paused");
            if (_hardReloadStartTimes.TryGetValue(wv, out var start))
            {
                var remaining = t.Interval - (DateTimeOffset.Now - start);
                if (remaining > TimeSpan.Zero)
                    return string.Format(R.Get("HardReload_Active"), (int)remaining.TotalMinutes, remaining.Seconds.ToString("D2"));
            }
            return string.Empty;
        }

        private void UpdateHardReloadTooltip(WebView2 wv, ToolTip tooltip)
        {
            tooltip.Content = GetHardReloadTooltipText(wv) is { Length: > 0 } text ? text : null;
        }

        private void EnsureHardReloadUiTimer()
        {
            if (_hardReloadUiTimer is not null) return;
            _hardReloadUiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _hardReloadUiTimer.Tick += (_, _) =>
            {
                foreach (var (wv, update) in _hardReloadUiUpdaters) update();
            };
            _hardReloadUiTimer.Start();
        }
    }
}
