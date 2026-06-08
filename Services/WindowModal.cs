using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// 子ウィンドウを親に対して擬似モーダルで開くヘルパー (#157)。
    /// WinUI 3 には Window のモーダル表示が無いため、親を EnableWindow(false) で
    /// 無効化し、子が閉じたら必ず復帰させる（例外時もロックが残らない）。
    /// </summary>
    internal static class WindowModal
    {
        [DllImport("user32.dll")]
        private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

        public static void RunModal(Window owner, Window modal)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
            EnableWindow(hwnd, false);
            modal.Closed += (_, _) => EnableWindow(hwnd, true);
            modal.Activate();
        }
    }
}
