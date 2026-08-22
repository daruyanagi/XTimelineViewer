using System;
using System.IO;
using System.Linq;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// 拡張機能の置き場と移行（#396）。
    ///
    /// これは<b>利用者が入れたものを移す</b>処理なので、失敗の仕方が重要になる。
    /// 「移せなかった」はやり直せるが、「移す途中で消えた」は戻せない。
    /// 成功する道より、途中で転んだときに<b>元が残ること</b>を厚く確かめる。
    /// </summary>
    [Collection("AppLog")]      // AppLog を経由する
    public class ExtensionStoreTests : IDisposable
    {
        private readonly string _root;
        private readonly string _old;
        private readonly string _new;

        public ExtensionStoreTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "xtv-ext-" + Guid.NewGuid().ToString("N"));
            _old  = Path.Combine(_root, "install", "extensions");
            _new  = Path.Combine(_root, "data", "extensions");
            Directory.CreateDirectory(_root);
            AppLog.Initialize(Path.Combine(_root, "error.log"));
        }

        public void Dispose()
        {
            AppLog.Initialize(Path.Combine(Path.GetTempPath(), "xtv-test-log-sink.log"));
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
            catch { /* 一時ディレクトリの後始末。消せなくてもテスト結果には関係ない */ }
            GC.SuppressFinalize(this);
        }

        /// <summary>拡張機能らしいフォルダーを作る。</summary>
        private static string MakeExtension(string parent, string name, string marker = "v1")
        {
            var dir = Path.Combine(parent, name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "manifest.json"), $$"""{"name":"{{name}}","version":"{{marker}}"}""");
            File.WriteAllText(Path.Combine(dir, "background.js"), marker);
            Directory.CreateDirectory(Path.Combine(dir, "_locales", "ja"));
            File.WriteAllText(Path.Combine(dir, "_locales", "ja", "messages.json"), marker);
            return dir;
        }

        // ── 移行 ─────────────────────────────────────────────────────────

        [Fact]
        public void Migrate_MovesEverythingIncludingSubfolders()
        {
            MakeExtension(_old, "uBlock");
            MakeExtension(_old, "Stylus");

            var moved = ExtensionStore.Migrate(_old, _new, copyOnly: false);

            Assert.Equal(2, moved);
            Assert.True(File.Exists(Path.Combine(_new, "uBlock", "manifest.json")));
            Assert.True(File.Exists(Path.Combine(_new, "Stylus", "manifest.json")));
            // 入れ子も落とさないこと
            Assert.True(File.Exists(Path.Combine(_new, "uBlock", "_locales", "ja", "messages.json")));
            // 旧い場所からは消えていること（二重に読み込まれないため）
            Assert.False(Directory.Exists(Path.Combine(_old, "uBlock")));
        }

        [Fact]
        public void Migrate_CopyOnly_LeavesTheSource()
        {
            // MSIX の WindowsApps 配下は消せない
            MakeExtension(_old, "uBlock");

            var moved = ExtensionStore.Migrate(_old, _new, copyOnly: true);

            Assert.Equal(1, moved);
            Assert.True(File.Exists(Path.Combine(_new, "uBlock", "manifest.json")));
            Assert.True(File.Exists(Path.Combine(_old, "uBlock", "manifest.json")));

            // 入れ子も落とさないこと。移動の経路（Directory.Move）は同一ボリュームだと
            // 中身を触らずに済んでしまうため、複製の経路でこそ確かめる必要がある。
            Assert.True(File.Exists(Path.Combine(_new, "uBlock", "_locales", "ja", "messages.json")));
        }

        [Fact]
        public void Migrate_DoesNotOverwriteWhatIsAlreadyThere()
        {
            // 移行先の方が利用者の意図した最新。旧い方で上書きすると巻き戻る。
            MakeExtension(_old, "uBlock", marker: "古い");
            MakeExtension(_new, "uBlock", marker: "新しい");

            var moved = ExtensionStore.Migrate(_old, _new, copyOnly: false);

            Assert.Equal(0, moved);
            Assert.Equal("新しい", File.ReadAllText(Path.Combine(_new, "uBlock", "background.js")));
        }

        [Fact]
        public void Migrate_NothingToDo_ReturnsZero()
            => Assert.Equal(0, ExtensionStore.Migrate(_old, _new, copyOnly: false));

        [Fact]
        public void Migrate_MissingOldDir_DoesNotCreateAnything()
        {
            ExtensionStore.Migrate(Path.Combine(_root, "no-such-dir"), _new, copyOnly: false);
            Assert.False(Directory.Exists(_new));
        }

        [Fact]
        public void Migrate_OneFailure_DoesNotStopTheRest()
        {
            // 1 つ掴まれていても、残りは移す。
            // 掴まれている方を<b>先に処理させる</b>こと。後ろだと、途中で諦める実装でも
            // 通ってしまい、テストとして意味を成さない（名前順に処理される）。
            MakeExtension(_old, "A-Locked");
            MakeExtension(_old, "B-Fine");

            using (var hold = new FileStream(Path.Combine(_old, "A-Locked", "background.js"),
                                             FileMode.Open, FileAccess.Read, FileShare.None))
            {
                ExtensionStore.Migrate(_old, _new, copyOnly: false);
            }

            Assert.True(File.Exists(Path.Combine(_new, "B-Fine", "manifest.json")));
        }

        [Fact]
        public void Migrate_Failure_LeavesTheSourceIntact()
        {
            // いちばん大事。移せなかったものが消えていないこと。
            // 消えていたら利用者は拡張機能を失う。
            MakeExtension(_old, "A-Locked");

            using (var hold = new FileStream(Path.Combine(_old, "A-Locked", "background.js"),
                                             FileMode.Open, FileAccess.Read, FileShare.None))
            {
                ExtensionStore.Migrate(_old, _new, copyOnly: false);
                Assert.True(File.Exists(Path.Combine(_old, "A-Locked", "manifest.json")));
            }
        }

        [Fact]
        public void Migrate_IsIdempotent()
        {
            // 起動のたびに呼ばれる。2 回目以降が壊さないこと。
            MakeExtension(_old, "uBlock");

            Assert.Equal(1, ExtensionStore.Migrate(_old, _new, copyOnly: false));
            Assert.Equal(0, ExtensionStore.Migrate(_old, _new, copyOnly: false));
            Assert.Equal(0, ExtensionStore.Migrate(_old, _new, copyOnly: false));

            Assert.True(File.Exists(Path.Combine(_new, "uBlock", "manifest.json")));
        }

        // ── 読み込み対象の絞り込み ────────────────────────────────────────

        [Fact]
        public void Enumerate_SkipsFoldersWithoutManifest()
        {
            // 展開しそこなった残骸などを掴むと、起動のたびにエラーが出る
            MakeExtension(_new, "Good");
            Directory.CreateDirectory(Path.Combine(_new, "NotAnExtension"));
            File.WriteAllText(Path.Combine(_new, "NotAnExtension", "readme.txt"), "x");

            var dirs = ExtensionStore.EnumerateExtensionDirs(_new).ToList();

            Assert.Single(dirs);
            Assert.Equal("Good", Path.GetFileName(dirs[0]));
        }

        [Fact]
        public void Enumerate_MissingDir_ReturnsEmpty()
            => Assert.Empty(ExtensionStore.EnumerateExtensionDirs(Path.Combine(_root, "no-such-dir")));

        [Fact]
        public void Enumerate_IsStablyOrdered()
        {
            // 並びが起動ごとに変わると、読み込み順に依存する不具合の再現が難しくなる
            MakeExtension(_new, "Zebra");
            MakeExtension(_new, "alpha");
            MakeExtension(_new, "Mango");

            var names = ExtensionStore.EnumerateExtensionDirs(_new).Select(Path.GetFileName).ToList();

            Assert.Equal(["alpha", "Mango", "Zebra"], names);
        }
    }
}
