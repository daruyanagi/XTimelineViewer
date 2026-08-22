using System;
using System.IO;
using System.Text;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// error.log のローテーション（#374）。
    ///
    /// 手元の error.log は 82 MB / 103 万行まで育っていた。上限も削除も無いまま
    /// 追記し続けていたため。エラーを探すときノイズに埋もれるうえ、失敗ダイアログが
    /// 「ログ: &lt;パス&gt;」と案内しても開けない大きさになる。
    /// </summary>
    public class AppLogTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _file;

        public AppLogTests()
        {
            _dir  = Path.Combine(Path.GetTempPath(), "xtv-log-" + Guid.NewGuid().ToString("N"));
            _file = Path.Combine(_dir, "error.log");
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// テスト後は使い捨てのパスへ向ける。
        /// AppLog.Initialize() を引数無しで呼ぶと、実際の
        /// %LOCALAPPDATA%\XTimelineViewer\error.log をローテーションしてしまう。
        /// </summary>
        private static void ResetToSink()
            => AppLog.Initialize(Path.Combine(Path.GetTempPath(), "xtv-test-log-sink.log"));

        private void Write(long bytes, string fill = "x")
            => File.WriteAllText(_file, new StringBuilder().Insert(0, fill, (int)bytes).ToString());

        [Fact]
        public void RotateIfNeeded_UnderLimit_KeepsFile()
        {
            Write(100);
            AppLog.RotateIfNeeded(_file, maxBytes: 1000);

            Assert.True(File.Exists(_file));
            Assert.False(File.Exists(_file + ".1"));
        }

        [Fact]
        public void RotateIfNeeded_OverLimit_MovesToGenerationOne()
        {
            Write(2000, "a");
            AppLog.RotateIfNeeded(_file, maxBytes: 1000);

            Assert.False(File.Exists(_file));           // 追記時に作り直される
            Assert.True(File.Exists(_file + ".1"));
            Assert.StartsWith("aaa", File.ReadAllText(_file + ".1"));
        }

        [Fact]
        public void RotateIfNeeded_OverwritesPreviousGeneration()
        {
            // 世代は 1 つだけ。古い .1 は捨てる（調査用の一時ログなので長期保存しない）
            File.WriteAllText(_file + ".1", "old");
            Write(2000, "b");
            AppLog.RotateIfNeeded(_file, maxBytes: 1000);

            Assert.StartsWith("bbb", File.ReadAllText(_file + ".1"));
        }

        [Fact]
        public void RotateIfNeeded_MissingFile_DoesNothing()
        {
            AppLog.RotateIfNeeded(_file, maxBytes: 1000);
            Assert.False(File.Exists(_file));
            Assert.False(File.Exists(_file + ".1"));
        }

        [Fact]
        public void RotateIfNeeded_LockedFile_DoesNotThrow()
        {
            // 退避できなくても追記は続けたい。ここで投げると本題の例外が隠れる。
            Write(2000);
            using var hold = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.None);

            AppLog.RotateIfNeeded(_file, maxBytes: 1000);   // 例外を出さないこと
            Assert.True(File.Exists(_file));
        }

        [Fact]
        public void Initialize_RotatesOversizedLogAtStartup()
        {
            Write(2000, "c");
            try
            {
                AppLog.Initialize(_file, maxBytes: 1000);
                Assert.True(File.Exists(_file + ".1"));
            }
            finally
            {
                ResetToSink();
            }
        }

        [Fact]
        public void Error_And_Debug_AppendToConfiguredFile()
        {
            try
            {
                AppLog.Initialize(_file);
                AppLog.Error("Ctx", new InvalidOperationException("boom"));
                AppLog.Debug("hello");

                var text = File.ReadAllText(_file);
                Assert.Contains("Ctx", text);
                Assert.Contains("boom", text);
                Assert.Contains("DBG hello", text);
            }
            finally
            {
                ResetToSink();
            }
        }
    }
}
