using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using XTimelineViewer.Services;
using XTimelineViewer.Views;

namespace XTimelineViewer
{
    public partial class App : Application
    {
        // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
        // Must be called before InitializeComponent so WebView2 (Win32 HWND) and WinUI 3 (DIP)
        // coordinate systems are aligned, preventing scroll events hitting the wrong column on
        // non-100% DPI displays (125%, 150%, 200%).
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(nint value);

        private Window? _window;

        public App()
        {
            try
            {
                SetProcessDpiAwarenessContext(-4);
            }
            catch (Exception ex)
            {
                // ヘッドレス VM など DPI API が利用できない環境でもクラッシュしない
                Debug.WriteLine($"[App] SetProcessDpiAwarenessContext failed: {ex.Message}");
            }
            this.InitializeComponent();

            // ログの初期化は例外ハンドラーを張る前に。
            // ここで肥大化した error.log を 1 世代退避する（#374）。
            AppLog.Initialize();

            // UI スレッドの未処理例外でプロセスが即死するのを防ぐ。
            // winget バリデーション VM など特殊環境でのサイレントクラッシュを診断しやすくする。
            this.UnhandledException += (sender, e) =>
            {
                Debug.WriteLine($"[App] UnhandledException: {e.Exception}");
                AppLog.Error("UnhandledException", e.Exception);
                e.Handled = true;
            };
        }

        // 以前はここでパスを手書きで組み直していた。Services/AppLog.cs へ集約（#374）。

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // WinAppSDK 1.6+ の Microsoft.Windows.Globalization 経由で packaged / unpackaged
            // 両対応の言語上書きを行う（R.Initialize 内で設定）。リソース読み込み前に呼ぶこと。
            var lang = ReadLanguageSetting();
            R.Initialize(lang);
            _window = new MainWindow();
            _window.Activate();
        }

        private static string? ReadLanguageSetting()
        {
            try
            {
                var settingsPath = PackageContext.IsPackaged
                    ? Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "settings.json")
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "XTimelineViewer", "settings.json");

                if (!File.Exists(settingsPath)) return null;

                using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
                if (doc.RootElement.TryGetProperty("Language", out var lang) &&
                    lang.GetString() is { } langStr && langStr != "system")
                {
                    Debug.WriteLine($"[App] Language setting: {langStr}");
                    return langStr;
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] ReadLanguageSetting FAILED: {ex.Message}");
                return null;
            }
        }
    }
}
