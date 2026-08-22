using System;
using System.IO;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// error.log への追記を一手に引き受ける（#374）。
    ///
    /// 以前は同じファイルに書く実装が 2 つあり、パスの組み立ても書式も別々だった
    /// （App.LogUnhandledException と MainWindow.LogError）。App は MainWindow より
    /// 先に走るため分かれていたが、書式が揃わないので統合した。
    ///
    /// UI 依存なし。ローテーションはパスとサイズを引数で受け取り、単体でテストできる。
    /// </summary>
    internal static class AppLog
    {
        /// <summary>この大きさを超えたら世代交代する。</summary>
        internal const long DefaultMaxBytes = 1_000_000;

        // 追記のたびにサイズを見ると I/O が増えるので、一定回数ごとに確認する。
        // 起動時だけだと、長時間動かしっぱなしのセッションで上限を超え続ける。
        private const int RotateCheckInterval = 200;

        private static readonly object Gate = new();
        private static string _filePath = DefaultFilePath();
        private static long   _maxBytes = DefaultMaxBytes;
        private static int    _writesSinceCheck;

        internal static string FilePath => _filePath;

        /// <summary>
        /// 既定の保存先。パッケージ版でもここに置く（従来の場所を変えると
        /// 過去のログが取り残されるため）。
        /// </summary>
        internal static string DefaultFilePath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XTimelineViewer", "error.log");

        /// <summary>
        /// 起動時に 1 回呼ぶ。保存先を決め、必要なら世代交代する。
        /// </summary>
        internal static void Initialize(string? filePath = null, long maxBytes = DefaultMaxBytes)
        {
            lock (Gate)
            {
                _filePath = filePath ?? DefaultFilePath();
                _maxBytes = maxBytes;
                _writesSinceCheck = 0;
            }
            RotateIfNeeded(_filePath, _maxBytes);
        }

        /// <summary>例外を記録する。</summary>
        internal static void Error(string context, Exception ex)
            => Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");

        /// <summary>診断用の 1 行を記録する。</summary>
        internal static void Debug(string message)
            => Append($"[{DateTime.Now:HH:mm:ss}] DBG {message}{Environment.NewLine}");

        private static void Append(string text)
        {
            string path;
            long   max;
            bool   check;
            lock (Gate)
            {
                path = _filePath;
                max  = _maxBytes;
                check = ++_writesSinceCheck >= RotateCheckInterval;
                if (check) _writesSinceCheck = 0;
            }

            if (check) RotateIfNeeded(path, max);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, text);
            }
            catch { /* ログ書き込みの失敗は無視する。ここで投げると本題が隠れる */ }
        }

        /// <summary>
        /// ファイルが上限を超えていたら 1 世代だけ退避する。
        /// error.log → error.log.1（既にあれば上書き）。
        ///
        /// 世代を増やさないのは、これが調査用の一時ログで、
        /// 長期保存する必要が無いため。
        /// </summary>
        internal static void RotateIfNeeded(string filePath, long maxBytes)
        {
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists || info.Length < maxBytes) return;
                File.Move(filePath, filePath + ".1", overwrite: true);
            }
            catch { /* 退避できなくても追記は続ける */ }
        }
    }
}
