using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
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

        // 既存ウィンドウを前に出す（#435）。WinUI の Activate() と AppWindow.Show() では
        // 別プロセスからの要求で前面に来ないため、Win32 を直接呼ぶ。
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(nint hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(nint hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(nint hWnd);

        private const int SW_RESTORE = 9;

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
            return $"=== XTimelineViewer v{AppVersion()} ({ChannelName()}) "
                 + $"WinAppSDK={SafeProbe(WinAppSdkVersion)} WebView2={SafeProbe(WebView2Version)} "
                 + $"{RuntimeInformation.ProcessArchitecture} {Environment.OSVersion.VersionString} "
                 + $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        }

        internal static string AppVersion()
            => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

        internal static string ChannelName() => PackageContext.Channel switch
        {
            InstallChannel.Winget   => "winget",
            InstallChannel.Packaged => "packaged",
            _                       => "zip",
        };

        /// <summary>
        /// 報告に添える環境（#426）。ログの見出し（#340）と同じものを使う。
        /// あちらは実際に切り分けへ使ったものだけを集めてあるので、
        /// 別立てにすると片方だけ古くなる。
        /// </summary>
        internal static (string Label, string Value)[] DescribeEnvironment() =>
        [
            (R.Get("Feedback_AppVersion"),   $"v{AppVersion()} ({ChannelName()})"),
            ("Windows App SDK",              SafeProbe(WinAppSdkVersion)),
            ("WebView2",                     SafeProbe(WebView2Version)),
            (R.Get("Feedback_Architecture"), RuntimeInformation.ProcessArchitecture.ToString()),
            ("OS",                           Environment.OSVersion.VersionString),
        ];

        /// <summary>
        /// 環境をあらかじめ入れた新規 issue の URL（#426）。
        /// メニューとバージョン情報ページの両方から開くので、ここに 1 つだけ置く。
        /// </summary>
        internal static string FeedbackIssueUrl()
            => Services.FeedbackUrl.For(
                   Services.AppUrls.NewIssue,
                   Services.FeedbackUrl.BuildBody(DescribeEnvironment(), R.Get("Feedback_Symptoms")));

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

        /// <summary>
        /// Windows App SDK の版（#427）。
        ///
        /// 以前は <c>Microsoft.UI.Xaml.Application</c> のあるアセンブリ
        /// （<c>Microsoft.WinUI.dll</c>）の FileVersion を読んでいた。あれは
        /// <b>WinUI 3 自身の版</b>で、報告を受け取った側からは実在しない
        /// Windows App SDK の版（<c>3.0.0.2608</c>）に見えていた。WinUI 3 は
        /// UWP 時代の WinUI 2.x の続きで 3.x から始まっており、
        /// Windows App SDK の 1.x とは別系統に振られている。
        ///
        /// 正規の API は自己完結・unpackaged でもそのまま呼べる（実測）。
        /// 返るのは <c>1.8.804</c> のような版で、csproj の
        /// <c>1.8.260804001</c> と文字列としては一致しないが、
        /// <c>1.8</c> は一致し <c>804</c> は日付部分（2026-08-04）に対応する。
        /// </summary>
        private static string WinAppSdkVersion()
            => Microsoft.Windows.ApplicationModel.WindowsAppRuntime.ReleaseInfo.AsString;

        /// <summary>
        /// 既に動いているインスタンスがあれば、そちらへ活性化を渡す（#435）。
        /// 渡したら true。呼び出し元はそのまま戻って終了する。
        ///
        /// <b><c>RedirectActivationToAsync</c> を STA でそのまま待つと固まる。</b>
        /// スレッドプールで走らせ、こちらはセマフォで待つ（公式の推奨どおり）。
        /// 渡し終える前にプロセスが消えると、受け取る側が引数を読み切れずに
        /// RPC が落ちるので、完了を待ってから終わること。
        /// </summary>
        private bool RedirectToExistingInstance()
        {
            try
            {
                var keyInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);

                if (keyInstance.IsCurrent)
                {
                    // 代表側。2 つ目以降の起動はここへ回ってくる。
                    AppInstance.GetCurrent().Activated += OnRedirectedActivation;
                    return false;
                }

                var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                var done = new System.Threading.SemaphoreSlim(0, 1);

                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try { await keyInstance.RedirectActivationToAsync(activationArgs); }
                    catch (Exception ex) { AppLog.Error("SingleInstance.Redirect", ex); }
                    finally { done.Release(); }
                });

                // 渡し終えるまで待つ。先に消えると、受け取る側が引数を
                // 読み切れずに RPC が落ちる。
                done.Wait(TimeSpan.FromSeconds(10));

                // ここで終わらせる。OnLaunched から戻るだけでは終わらない。
                // ウィンドウを作っていなくてもメッセージループは回り続け、
                // 見えないプロセスが起動のたびに積み上がる（実際にそうなった）。
                Process.GetCurrentProcess().Kill();
                return true;
            }
            catch (Exception ex)
            {
                // 単一インスタンス化に失敗しても起動は続ける。
                // ここで諦めると、アプリが一切立ち上がらなくなる。
                AppLog.Error("SingleInstance", ex);
                return false;
            }
        }

        /// <summary>
        /// 2 つ目の起動から渡されたときに、こちらのウィンドウを前へ出す（#435）。
        ///
        /// <b>この通知は UI スレッドには来ない。</b> ウィンドウに触る前に移すこと。
        /// </summary>
        private void OnRedirectedActivation(object? sender, AppActivationArguments e)
        {
            // この通知は UI スレッドには来ない。
            _window?.DispatcherQueue.TryEnqueue(() =>
            {
                BringToFront();
                HandleShareIfAnyAsync(e).FireAndForget(nameof(HandleShareIfAnyAsync));
            });
        }

        /// <summary>
        /// 共有シートからの起動なら、中身を下書きにして投稿ダイアログを開く（#431）。
        /// それ以外の起動では何もしない。
        /// </summary>
        private async Task HandleShareIfAnyAsync(AppActivationArguments args)
        {
            if (args?.Kind != ExtendedActivationKind.ShareTarget) return;
            if (args.Data is not Windows.ApplicationModel.Activation.ShareTargetActivatedEventArgs share) return;

            var op = share.ShareOperation;

            try
            {
                var draft = await SharePayload.ReadDraftAsync(op.Data);
                AppLog.Debug($"Share: 受け取った（下書き {(draft is null ? "無し" : $"{draft.Length} 文字")}）");

                if (draft is not null && _window is MainWindow main)
                    await main.OpenPostWithDraftAsync(draft);
            }
            catch (Exception ex)
            {
                AppLog.Error("Share", ex);
            }
            finally
            {
                // 下書きを渡し終えてから報告する。先に報告すると、共有元から見て
                // 終わったことになり、読み出し途中の中身が消えうる。
                try { op.ReportCompleted(); }
                catch (Exception ex) { AppLog.Debug($"Share: ReportCompleted に失敗 {ex.Message}"); }
            }
        }

        private void BringToFront()
        {
            if (_window is null) return;

            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);

                // 最小化されていたら戻す。しないと前面に出しても見えない。
                if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);

                SetForegroundWindow(hwnd);
            }
            catch (Exception ex)
            {
                AppLog.Error("SingleInstance.BringToFront", ex);
            }
        }

        /// <summary>
        /// 単一インスタンスの鍵（#435）。
        /// 配布経路（ZIP / winget）ごとに分けない。同じ利用者のデータを
        /// 2 プロセスで掴むのを防ぐのが目的なので、経路が違っても 1 つにする。
        /// </summary>
        private const string SingleInstanceKey = "XTimelineViewer";

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

            // 2 つ目以降の起動は、既にいる方へ渡して自分は消える（#435）。
            // 同じプロファイルフォルダーを 2 プロセスで掴むと WebView2 の
            // 拡張機能登録が壊れうる。#419 / #420 の調査でも混入を疑う場面があった。
            //
            // 必ず --finish-update の判定より後に置くこと。仕上げ役は
            // 別プロセスとして走る必要があり、ここで畳んでは更新が止まる。
            if (RedirectToExistingInstance()) return;

            // WinAppSDK 1.6+ の Microsoft.Windows.Globalization 経由で packaged / unpackaged
            // 両対応の言語上書きを行う（R.Initialize 内で設定）。リソース読み込み前に呼ぶこと。
            var lang = ReadLanguageSetting();
            R.Initialize(lang);

            // 前回の更新で残った .old を片付ける。消せなくても支障は無い。
            UpdateSwap.CleanupBackup(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));

            _window = new MainWindow();
            _window.Activate();

            // 共有シートから起動された場合は、受け取った中身を下書きにして
            // 投稿ダイアログを開く（#431）。ウィンドウを出してから。
            HandleShareIfAnyAsync(AppInstance.GetCurrent().GetActivatedEventArgs())
                .FireAndForget(nameof(HandleShareIfAnyAsync));
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
