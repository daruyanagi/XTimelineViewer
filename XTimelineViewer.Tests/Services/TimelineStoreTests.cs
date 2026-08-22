using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XTimelineViewer.Models;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// timelines.json の永続化（#368）。
    ///
    /// このファイルが壊れると Load が例外を握りつぶすため、全ペインが黙って消える。
    /// #338 で入れた 3 つの不変条件を、ここで固定する。
    /// </summary>
    public class TimelineStoreTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _file;

        public TimelineStoreTests()
        {
            _dir  = Path.Combine(Path.GetTempPath(), "xtv-tests-" + Guid.NewGuid().ToString("N"));
            _file = Path.Combine(_dir, "timelines.json");
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
            GC.SuppressFinalize(this);
        }

        private static List<TimelineConfig> Sample(params string[] urls)
            => urls.Select(u => new TimelineConfig { Url = u }).ToList();

        // ── Load ──────────────────────────────────────────────────────────────

        [Fact]
        public void Load_MissingFile_ReturnsEmpty()
            => Assert.Empty(TimelineStore.Load(_file));

        [Fact]
        public void Load_BrokenJson_ReturnsEmpty()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(_file, "{ this is not valid json");
            Assert.Empty(TimelineStore.Load(_file));
        }

        [Fact]
        public async Task SaveThenLoad_RoundTripsAllProperties()
        {
            var cfg = new TimelineConfig
            {
                Url                = "https://x.com/home",
                Width              = 480,
                HideSidebar        = true,
                HideCompose        = false,
                HideListHeader     = true,
                HardReloadEnabled  = true,
                HardReloadInterval = 7,
                ProfileId          = "abc123",
                IsListsIndex       = true,
            };
            await TimelineStore.SaveAsync(_file, new[] { cfg });

            var loaded = Assert.Single(TimelineStore.Load(_file));
            Assert.Equal(cfg.Url,                loaded.Url);
            Assert.Equal(cfg.Width,              loaded.Width);
            Assert.Equal(cfg.HideSidebar,        loaded.HideSidebar);
            Assert.Equal(cfg.HideCompose,        loaded.HideCompose);
            Assert.Equal(cfg.HideListHeader,     loaded.HideListHeader);
            Assert.Equal(cfg.HardReloadEnabled,  loaded.HardReloadEnabled);
            Assert.Equal(cfg.HardReloadInterval, loaded.HardReloadInterval);
            Assert.Equal(cfg.ProfileId,          loaded.ProfileId);
            Assert.Equal(cfg.IsListsIndex,       loaded.IsListsIndex);
        }

        [Fact]
        public async Task SaveThenLoad_PreservesOrder()
        {
            // 並び順 = 表示順 = 番号バッジの順（#225 / #359）。ここが崩れると
            // 復元時にペインの並びが変わる。
            var urls = new[] { "https://x.com/home", "https://x.com/notifications", "https://x.com/i/bookmarks" };
            await TimelineStore.SaveAsync(_file, Sample(urls));
            Assert.Equal(urls, TimelineStore.Load(_file).Select(c => c.Url));
        }

        // ── #338 の不変条件 1 / 3（構造テスト）─────────────────
        //
        // この 2 つは意図的にソースの文字列走査にしている。振る舞いテストでは
        // 壊れても検出できないことを、実際に壊して確かめた。
        //
        //   不変条件 1 … SemaphoreSlim.WaitAsync() は無競合なら同期完了する。
        //     シリアライズをロックの中へ移しても、単一スレッドのテストからは
        //     振る舞いが区別できない。
        //   不変条件 3 … tmp を使わず直接書きにしても、「tmp が残っていない」という
        //     テストは通ってしまう（そもそも tmp を作らないため）。本来の性質
        //     「書き込みが中断しても既存ファイルが壊れない」は、障害を注入
        //     できないと確かめられない。
        //
        // KeyboardShortcutDriftTests と同じやり方で、実装の形を固定する。

        private static string StoreSource()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "Services", "TimelineStore.cs");
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
                dir = dir.Parent;
            }
            throw new FileNotFoundException("Services/TimelineStore.cs が見つかりません");
        }

        [Fact]
        public void SaveAsync_SerializesBeforeFirstAwait()
        {
            var src        = StoreSource();
            var serialize  = src.IndexOf("JsonSerializer.Serialize(configs", StringComparison.Ordinal);
            var firstAwait = src.IndexOf("await SaveLock.WaitAsync()", StringComparison.Ordinal);

            Assert.True(serialize >= 0 && firstAwait >= 0,
                "SaveAsync の形が変わっています。テストを見直してください。");
            Assert.True(serialize < firstAwait,
                "シリアライズが最初の await より後ろにあります。" +
                "保存内容が呼び出し時点のスナップショットではなくなります（#338）。");
        }

        [Fact]
        public void SaveAsync_WritesToTempThenMoves()
        {
            var src = StoreSource();
            Assert.Contains("WriteAllTextAsync(tmp", src);
            Assert.Contains("File.Move(tmp, filePath, overwrite: true)", src);
            Assert.DoesNotContain("WriteAllTextAsync(filePath", src);
        }

        // ── 振る舞いテスト ──────────────────────────────────

        [Fact]
        public async Task SaveAsync_LeavesNoTempFileBehind()
        {
            await TimelineStore.SaveAsync(_file, Sample("https://x.com/home"));
            Assert.False(File.Exists(_file + ".tmp"), "一時ファイルが残っています。");
        }


        [Fact]
        public async Task SaveAsync_OverwritesExistingFile()
        {
            await TimelineStore.SaveAsync(_file, Sample("https://x.com/home"));
            await TimelineStore.SaveAsync(_file, Sample("https://x.com/notifications", "https://x.com/i/bookmarks"));

            var loaded = TimelineStore.Load(_file);
            Assert.Equal(2, loaded.Count);
            Assert.Equal("https://x.com/notifications", loaded[0].Url);
        }

        [Fact]
        public async Task SaveAsync_CreatesMissingDirectory()
        {
            Assert.False(Directory.Exists(_dir));
            await TimelineStore.SaveAsync(_file, Sample("https://x.com/home"));
            Assert.True(File.Exists(_file));
        }

        // ── 不変条件 2: 保存の直列化 ────────────────────────────────────

        [Fact]
        public async Task SaveAsync_ConcurrentCalls_LeaveValidJson()
        {
            // fire-and-forget で重なっても壊れないこと。直列化が無いと
            // 書きかけの内容を読むレースが起きうる。
            var tasks = Enumerable.Range(0, 20)
                .Select(i => TimelineStore.SaveAsync(_file, Sample($"https://x.com/t{i}")))
                .ToArray();
            await Task.WhenAll(tasks);

            var loaded = TimelineStore.Load(_file);
            Assert.Single(loaded);
            Assert.StartsWith("https://x.com/t", loaded[0].Url);
            Assert.False(File.Exists(_file + ".tmp"));
        }
    }
}
