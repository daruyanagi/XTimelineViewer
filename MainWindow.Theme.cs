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

namespace XTimelineViewer
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

        private void ApplyThemeToWebViews()
        {
            var root   = (FrameworkElement)Content;
            var scheme = root.RequestedTheme switch
            {
                ElementTheme.Light => CoreWebView2PreferredColorScheme.Light,
                ElementTheme.Dark  => CoreWebView2PreferredColorScheme.Dark,
                _                  => CoreWebView2PreferredColorScheme.Auto,
            };
            foreach (var wv in _webViews)
                if (wv.CoreWebView2 is not null)
                    wv.CoreWebView2.Profile.PreferredColorScheme = scheme;
        }
    }
}
