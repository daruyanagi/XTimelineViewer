using System;
using System.IO;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// ログの追記を一手に引き受ける（#374）。
    ///
    /// 以前は同じファイルに書く実装が 2 つあり、パスの組み立ても書式も別々だった
    /// （App.LogUnhandledException と MainWindow.LogError）。App は MainWindow より
    /// 先に走るため分かれていたが、書式が揃わないので統合した。
    ///
    /// <b>行き先は 2 つ（#414）。</b>
    /// <list type="bullet">
    ///   <item><c>error.log</c> … 例外と、節目の 1 行（更新・拡張機能など）</item>
    ///   <item><c>diag.log</c> … 量の出る調査用（動画DL の GraphQL 傍受など）</item>
    /// </list>
    ///
    /// 分けたのは、調査用の行が毎秒のように出るため。混ぜると 1 MB × 2 世代が
    /// 半日で一周し、例外の履歴が残らない。実測（v2.0.4）で error.log 16,022 行の
    /// うち 15,343 行が調査用で、<b>UnhandledException の記録は 1 件も残っていなかった</b>。
    ///
    /// UI 依存なし。ローテーションはパスとサイズを引数で受け取り、単体でテストできる。
    /// </summary>
    internal static class AppLog
    {
        /// <summary>この大きさを超えたら世代交代する。</summary>
        internal const long DefaultMaxBytes = 1_000_000;

        private static readonly Sink AppSink  = new();
        private static readonly Sink DiagSink = new();

        static AppLog() => SetPaths(DefaultFilePath(), DefaultMaxBytes);

        internal static string FilePath     => AppSink.Path;
        internal static string DiagFilePath => DiagSink.Path;

        /// <summary>
        /// 既定の保存先。パッケージ版でもここに置く（従来の場所を変えると
        /// 過去のログが取り残されるため）。
        /// </summary>
        internal static string DefaultFilePath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XTimelineViewer", "error.log");

        /// <summary>
        /// 調査用ログの置き場。error.log の隣に置く（#414）。
        /// 報告を頼むときに「あのフォルダーの中身」で済ませたいので、離さない。
        /// </summary>
        internal static string DiagPathFor(string appLogPath)
            => Path.Combine(Path.GetDirectoryName(appLogPath) ?? ".", "diag.log");

        /// <summary>
        /// 起動時に 1 回呼ぶ。保存先を決め、必要なら世代交代する。
        /// </summary>
        internal static void Initialize(string? filePath = null, long maxBytes = DefaultMaxBytes)
        {
            SetPaths(filePath ?? DefaultFilePath(), maxBytes);
            AppSink.RotateNow();
            DiagSink.RotateNow();
        }

        private static void SetPaths(string appLogPath, long maxBytes)
        {
            AppSink.Reset(appLogPath, maxBytes);
            DiagSink.Reset(DiagPathFor(appLogPath), maxBytes);
        }

        /// <summary>
        /// ログの先頭に添えるセッション情報を登録する（#340）。
        /// アプリのバージョンや配布経路が分からないと、ログを見ても
        /// 切り分けができない。組み立ては呼び出し側の責任（ここは UI 非依存に保つ）。
        ///
        /// 両方に出す。片方だけ見て版が分からないと、結局もう片方を探すことになる。
        /// </summary>
        internal static void SetSessionHeader(string? header)
        {
            AppSink.SetHeader(header);
            DiagSink.SetHeader(header);
        }

        /// <summary>例外を記録する。</summary>
        internal static void Error(string context, Exception ex)
            => AppSink.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");

        /// <summary>診断用の 1 行を記録する。節目に出るものだけ。</summary>
        internal static void Debug(string message)
            => AppSink.Append($"[{DateTime.Now:HH:mm:ss}] DBG {message}{Environment.NewLine}");

        /// <summary>
        /// 量の出る調査用の 1 行（#414）。<c>diag.log</c> へ。
        ///
        /// <b>頻度が読めないものはこちらに出す。</b>
        /// error.log に混ぜると、そちらの履歴を押し流してしまう。
        /// </summary>
        internal static void Diag(string message)
            => DiagSink.Append($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

        /// <summary>
        /// ファイルが上限を超えていたら 1 世代だけ退避する。
        /// error.log → error.log.1（既にあれば上書き）。
        ///
        /// 世代を増やさないのは、これが調査用の一時ログで、
        /// 長期保存する必要が無いため。
        /// </summary>
        /// <returns>実際に退避したら true。見出しを出し直す判断に使う。</returns>
        internal static bool RotateIfNeeded(string filePath, long maxBytes)
        {
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists || info.Length < maxBytes) return false;
                File.Move(filePath, filePath + ".1", overwrite: true);
                return true;
            }
            catch { /* 退避できなくても追記は続ける */ }
            return false;
        }

        /// <summary>
        /// 1 ファイル分の状態。パスごとに見出しと退避の勘定を持つ必要があるので、
        /// 静的フィールドを並べるのをやめてここへ寄せた（#414）。
        /// </summary>
        private sealed class Sink
        {
            // 追記のたびにサイズを見ると I/O が増えるので、一定回数ごとに確認する。
            // 起動時だけだと、長時間動かしっぱなしのセッションで上限を超え続ける。
            private const int RotateCheckInterval = 200;

            private readonly object _gate = new();
            private string _path = string.Empty;
            private long   _maxBytes = DefaultMaxBytes;
            private int    _writesSinceCheck;

            // セッションの見出し（バージョン・配布経路など）。
            // 何も起きない起動で行を増やさないよう、最初の書き込み直前に一度だけ出す。
            private string? _header;
            private bool    _headerWritten;

            internal string Path { get { lock (_gate) return _path; } }

            internal void Reset(string path, long maxBytes)
            {
                lock (_gate)
                {
                    _path = path;
                    _maxBytes = maxBytes;
                    _writesSinceCheck = 0;
                    _headerWritten = false;
                }
            }

            internal void RotateNow()
            {
                string path; long max;
                lock (_gate) { path = _path; max = _maxBytes; }
                RotateIfNeeded(path, max);
            }

            internal void SetHeader(string? header)
            {
                lock (_gate)
                {
                    _header = header;
                    _headerWritten = false;
                }
            }

            internal void Append(string text)
            {
                string path;
                long   max;
                bool   check;
                lock (_gate)
                {
                    path = _path;
                    max  = _maxBytes;
                    check = ++_writesSinceCheck >= RotateCheckInterval;
                    if (check) _writesSinceCheck = 0;
                }

                // 退避したら見出しを出し直す。さもないと、長く起動したときに
                // 手元に残る error.log がちょうど「見出しの無い方」になる。
                if (check && RotateIfNeeded(path, max))
                {
                    lock (_gate) _headerWritten = false;
                }

                // セッション見出しは各ファイルの先頭に 1 回だけ。
                string? header = null;
                lock (_gate)
                {
                    if (!_headerWritten && _header is not null)
                    {
                        header = _header + Environment.NewLine;
                        _headerWritten = true;
                    }
                }

                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    if (header is not null) File.AppendAllText(path, header);
                    File.AppendAllText(path, text);
                }
                catch { /* ログ書き込みの失敗は無視する。ここで投げると本題が隠れる */ }
            }
        }
    }
}
