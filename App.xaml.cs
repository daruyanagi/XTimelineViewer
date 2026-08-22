using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
            AppLog.SetSessionHeader(BuildSessionHeader());

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

        /// <summary>
        /// ログの先頭に出すセッション情報（#340）。
        ///
        /// 未処理例外は現状 <c>e.Handled = true</c> で握りつぶしている。
        /// どの例外を致命的とみなすかの判断材料が無いためだが、
        /// ログに例外だけが並んでいても、どの版・どの基盤で起きたのか
        /// 分からないと切り分けられない。
        ///
        /// ここで集めるものは、実際に障害の切り分けに使ったものだけ。
        /// WebView2 版は X の描画崩れ、arm64/x64 は #267 のような混入事故の容疑者になる。
        /// </summary>
        private static string BuildSessionHeader()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
            var channel = PackageContext.Channel switch
            {
                InstallChannel.Winget   => "winget",
                InstallChannel.Packaged => "packaged",
                _                       => "zip",
            };

            return $"=== XTimelineViewer v{version} ({channel}) "
                 + $"WinAppSDK={SafeProbe(WinAppSdkVersion)} WebView2={SafeProbe(WebView2Version)} "
                 + $"{RuntimeInformation.ProcessArchitecture} {Environment.OSVersion.VersionString} "
                 + $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        }

        /// <summary>
        /// 見出しの組み立てで落ちないこと。
        /// ここは例外ハンドラーを張る前の起動経路なので、
        /// ログを見やすくするための処理で起動を壊しては本末転倒。
        /// </summary>
        private static string SafeProbe(Func<string> probe)
        {
            try { return probe(); } catch { return "?"; }
        }

        private static string WebView2Version()
            => CoreWebView2Environment.GetAvailableBrowserVersionString();

        private static string WinAppSdkVersion()
            => FileVersionInfo.GetVersionInfo(typeof(Microsoft.UI.Xaml.Application).Assembly.Location).FileVersion
               ?? "?";

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // 更新の仕上げ役として起動されたときは、UI を出さずに差し替えだけ行って終わる（#328）。
            // このプロセスは展開先（インストール先の隣）から動いており、
            // 差し替え対象の外にいるので、実行中の exe / DLL を掴んでいない。
            if (UpdateSwap.ParseFinishArgs(Environment.GetCommandLineArgs()[1..]) is { } finish)
            {
                FinishUpdateAsync(finish.InstallDir, finish.WaitForPid)
                    .FireAndForget(nameof(FinishUpdateAsync));
                return;
            }

            // WinAppSDK 1.6+ の Microsoft.Windows.Globalization 経由で packaged / unpackaged
            // 両対応の言語上書きを行う（R.Initialize 内で設定）。リソース読み込み前に呼ぶこと。
            var lang = ReadLanguageSetting();
            R.Initialize(lang);

            // 前回の更新で残った .old を片付ける。消せなくても支障は無い。
            UpdateSwap.CleanupBackup(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));

            _window = new MainWindow();
            _window.Activate();
        }

        /// <summary>
        /// 旧プロセスの終了を待って差し替え、本来の場所から起動し直す（#328）。
        ///
        /// 失敗しても旧版が起動できる状態に戻すのが最優先。
        /// 「更新できなかった」はやり直せるが、「更新に失敗して壊れた」は戻せない。
        /// </summary>
        private static async Task FinishUpdateAsync(string installDir, int waitForPid)
        {
            var staging = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            AppLog.Debug($"FinishUpdate: staging={staging} install={installDir} pid={waitForPid}");

            // 待ちきれないまま差し替えると、掴まれたままのファイルを動かすことになる。
            if (!await UpdateSwap.WaitForExitAsync(waitForPid, TimeSpan.FromSeconds(30)))
            {
                AppLog.Debug("FinishUpdate: 旧プロセスが終わらないので差し替えを中止する");
                LaunchAndExit(Path.Combine(installDir, "XTimelineViewer.exe"));
                return;
            }

            var result = UpdateSwap.Swap(installDir, staging);
            AppLog.Debug($"FinishUpdate: {result}");

            // Broken のときは installDir に何も無い。起動しても失敗するが、
            // ここで黙って終わるより、ログを残したうえで試みたほうが手がかりが残る。
            LaunchAndExit(Path.Combine(installDir, "XTimelineViewer.exe"));
        }

        private static void LaunchAndExit(string exePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName         = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath)!,
                    UseShellExecute  = true,
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("FinishUpdate(launch)", ex);
            }
            Current.Exit();
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
