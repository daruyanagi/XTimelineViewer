using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Runtime.InteropServices;
using Windows.UI.ViewManagement;

using XTimelineViewer.Views.Controls;

namespace XTimelineViewer.Views
{
    public sealed partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        // Win32 タイトルバーは WinUI の RequestedTheme の影響を受けないため
        // DWM 属性で直接ダークモードを指定する。子ウィンドウにも適用できるよう static に。
        internal static void ApplyTitleBarTheme(Window window, ElementTheme theme)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var dark = theme == ElementTheme.Dark ? 1
                     : theme == ElementTheme.Light ? 0
                     : (Application.Current.RequestedTheme == ApplicationTheme.Dark ? 1 : 0);
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }

        // High Contrast の検出（#341）。
        // AccessibilitySettings はデスクトップではウィンドウ初期化が要るため、
        // 依存の少ない SystemParametersInfo を使う。
        [StructLayout(LayoutKind.Sequential)]
        private struct HIGHCONTRAST
        {
            public uint   cbSize;
            public uint   dwFlags;
            public IntPtr lpszDefaultScheme;
        }
        private const uint SPI_GETHIGHCONTRAST = 0x0042;
        private const uint HCF_HIGHCONTRASTON  = 0x00000001;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(
            uint uiAction, uint uiParam, ref HIGHCONTRAST pvParam, uint fWinIni);

        internal static bool IsHighContrast()
        {
            var hc = new HIGHCONTRAST { cbSize = (uint)Marshal.SizeOf<HIGHCONTRAST>() };
            return SystemParametersInfo(SPI_GETHIGHCONTRAST, hc.cbSize, ref hc, 0)
                && (hc.dwFlags & HCF_HIGHCONTRASTON) != 0;
        }

        // コントラストテーマの切り替えに追従する（#341）。
        // ActualThemeChanged は Light/Dark の切り替えしか見ないため、
        // システム色の変化を UISettings で拾う。別スレッドで発火するので
        // DispatcherQueue で UI スレッドへ戻す。
        private readonly UISettings _uiSettings = new();

        private void HookContrastThemeChanges()
        {
            _uiSettings.ColorValuesChanged += (_, _) =>
                DispatcherQueue.TryEnqueue(() =>
                {
                    RefreshPaneThemes();
                    RefreshAllProfileBadges();
                });
        }

        /// <summary>1 ペインの配色を当て直す。フォーカス判定と HC 判定はこちらの責任。</summary>
        private void ApplyPaneTheme(TimelinePane pane)
            => pane.ApplyTheme(pane.ActualTheme, pane == _focusedPane, IsHighContrast());

        /// <summary>全ペインの配色を当て直す。フォーカス移動やテーマ変更のときに呼ぶ。</summary>
        private void RefreshPaneThemes()
        {
            foreach (var pane in Panes) ApplyPaneTheme(pane);
        }

        private void ApplySavedTheme()
        {
            var theme = _appSettings.Theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark"  => ElementTheme.Dark,
                _       => ElementTheme.Default,
            };
            ((FrameworkElement)Content).RequestedTheme = theme;
            ApplyTitleBarTheme(this, theme);
            ApplyThemeToWebViews();
        }

        private void ThemeItem_Click(object sender, RoutedEventArgs _)
        {
            if (sender is RadioMenuFlyoutItem item && item.Tag is string theme)
            {
                _appSettings.Theme = theme;
                SaveSettings();
                ApplySavedTheme();
            }
        }

        private void UpdateThemeRadioState()
        {
            ThemeSystemItem.IsChecked = _appSettings.Theme is null or "Default";
            ThemeLightItem.IsChecked  = _appSettings.Theme == "Light";
            ThemeDarkItem.IsChecked   = _appSettings.Theme == "Dark";
        }

        private void ApplyThemeToWebViews()
        {
            var root   = (FrameworkElement)Content;
            var scheme = root.RequestedTheme switch
            {
                ElementTheme.Light => CoreWebView2PreferredColorScheme.Light,
                ElementTheme.Dark  => CoreWebView2PreferredColorScheme.Dark,
                _                  => CoreWebView2PreferredColorScheme.Auto,
            };
            foreach (var pane in Panes)
                if (pane.WebView.CoreWebView2 is not null)
                    pane.WebView.CoreWebView2.Profile.PreferredColorScheme = scheme;
        }
    }
}
