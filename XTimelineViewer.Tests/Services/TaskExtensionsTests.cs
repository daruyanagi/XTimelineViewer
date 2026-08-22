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

        private async Task<string> WaitForLogAsync()
        {
            // FireAndForget は待たない設計なので、記録されるまで少しだけ待つ
            for (int i = 0; i < 50; i++)
            {
                if (File.Exists(_file))
                {
                    var text = File.ReadAllText(_file);
                    if (text.Length > 0) return text;
                }
                await Task.Delay(20);
            }
            return File.Exists(_file) ? File.ReadAllText(_file) : string.Empty;
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
            Assert.False(File.Exists(_file) && File.ReadAllText(_file).Length > 0);
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
