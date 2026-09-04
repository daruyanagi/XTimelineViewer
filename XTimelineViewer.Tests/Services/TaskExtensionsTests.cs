using System;
using System.IO;
using System.Threading.Tasks;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// 待たない非同期処理の失敗を記録する（#374）。
    ///
    /// <c>_ = SomethingAsync()</c> と書くと例外を誰も観測しない。#339 はまさにこれで、
    /// InitWebViewAsync の後半 90 行が try の外にあり、失敗が完全に無言だった。
    /// </summary>
    // AppLog は静的なので、同じシンクを取り合うクラスを並列に走らせない。
    // 並列だと Initialize の先方が互いの出力先を奇麗にしてしまい、結果が揺れる。
    [Collection("AppLog")]
    public class TaskExtensionsTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _file;

        public TaskExtensionsTests()
        {
            _dir  = Path.Combine(Path.GetTempPath(), "xtv-faf-" + Guid.NewGuid().ToString("N"));
            _file = Path.Combine(_dir, "error.log");
            Directory.CreateDirectory(_dir);
            AppLog.Initialize(_file);
        }

        public void Dispose()
        {
            // 既定パス（実際の error.log）へ戻さないこと。ローテーションしてしまう。
            AppLog.Initialize(Path.Combine(Path.GetTempPath(), "xtv-test-log-sink.log"));
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 書き込み中でも読めるように開く。
        ///
        /// <b><c>File.ReadAllText</c> は使えない。</b>あちらは共有指定が
        /// <c>FileShare.Read</c> で「他に書き手がいないこと」を要求する。
        /// FireAndForget の記録は別スレッドの継続から入るので、
        /// 読みと書きが重なった瞬間に
        /// 「別のプロセスで使用されているため…」で落ちる。
        /// 待たない処理を待たずに読むテストなので、重なるのは正常な動作。
        /// CI で実際に踏んだ（run 33844639439）。
        ///
        /// 眠らせて誤魔化さないこと。頻度が下がるだけで直らないし、
        /// テストが遅くなる。
        /// </summary>
        private static string ReadWhileWritable(string path)
        {
            using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd();
        }

        private static bool HasContent(string path)
            => File.Exists(path) && ReadWhileWritable(path).Length > 0;

        private async Task<string> WaitForLogAsync()
        {
            // FireAndForget は待たない設計なので、記録されるまで少しだけ待つ
            for (int i = 0; i < 50; i++)
            {
                if (File.Exists(_file))
                {
                    var text = ReadWhileWritable(_file);
                    if (text.Length > 0) return text;
                }
                await Task.Delay(20);
            }
            return File.Exists(_file) ? ReadWhileWritable(_file) : string.Empty;
        }

        [Fact]
        public async Task FireAndForget_FailedTask_IsLogged()
        {
            Task.FromException(new InvalidOperationException("boom")).FireAndForget("MyContext");

            var text = await WaitForLogAsync();
            Assert.Contains("FireAndForget(MyContext)", text);
            Assert.Contains("boom", text);
        }

        [Fact]
        public async Task FireAndForget_SucceededTask_LogsNothing()
        {
            Task.CompletedTask.FireAndForget("MyContext");

            await Task.Delay(100);
            Assert.False(HasContent(_file));
        }

        [Fact]
        public async Task FireAndForget_DoesNotThrowToCaller()
        {
            // 呼び出し元へ例外を伝播させない。ここで投げると UI イベントが落ちる。
            var ex = Record.Exception(() =>
                Task.FromException(new InvalidOperationException("boom")).FireAndForget("Ctx"));

            Assert.Null(ex);
            await WaitForLogAsync();
        }

        [Fact]
        public async Task FireAndForget_AsyncFailure_IsLogged()
        {
            // 同期的に失敗する Task ではなく、await の後で落ちる場合も拾えること
            static async Task Boom()
            {
                await Task.Delay(10);
                throw new TimeoutException("late");
            }
            Boom().FireAndForget("Late");

            var text = await WaitForLogAsync();
            Assert.Contains("FireAndForget(Late)", text);
            Assert.Contains("late", text);
        }
    }
}
