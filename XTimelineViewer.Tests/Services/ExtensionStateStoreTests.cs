using System;
using System.Collections.Generic;
using System.IO;
using XTimelineViewer.Models;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// 拡張機能の有効・無効（#398）。
    ///
    /// 判断の規則が UI とプロファイル読み込みで食い違うと
    /// 「設定では ON なのにペインでは効かない」が起きる。規則をここで固定する。
    /// </summary>
    [Collection("AppLog")]
    public class ExtensionStateStoreTests : IDisposable
    {
        private readonly string _dir;
        private readonly Dictionary<string, ExtensionState> _states = [];

        public ExtensionStateStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "xtv-state-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            AppLog.Initialize(Path.Combine(_dir, "error.log"));
        }

        public void Dispose()
        {
            AppLog.Initialize(Path.Combine(Path.GetTempPath(), "xtv-test-log-sink.log"));
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
            catch { /* 一時ディレクトリの後始末。消せなくてもテスト結果には関係ない */ }
            GC.SuppressFinalize(this);
        }

        // ── 既定の振る舞い ───────────────────────────────────────────────

        [Fact]
        public void Unknown_IsEnabled()
        {
            // 記録の無い拡張機能は有効。入れたものがそのまま効く、という
            // 単純な理解のまま使えることが要点。
            Assert.True(ExtensionStateStore.IsEnabled(_states, "uBlock", "profile-a"));
        }

        [Fact]
        public void UnknownProfile_FollowsTheExtensionDefault()
        {
            ExtensionStateStore.SetEnabledByDefault(_states, "uBlock", false);

            // 触っていないプロファイルは、その拡張機能の既定に従う
            Assert.False(ExtensionStateStore.IsEnabled(_states, "uBlock", "誰も触っていないプロファイル"));
        }

        [Fact]
        public void NewProfile_UsesTheDefault_WhichIsEnabled()
        {
            // 既定を有効にしてあるので、新しいプロファイルでも黙って効く
            ExtensionStateStore.SetEnabled(_states, "uBlock", "profile-a", false);

            Assert.False(ExtensionStateStore.IsEnabled(_states, "uBlock", "profile-a"));
            Assert.True(ExtensionStateStore.IsEnabled(_states, "uBlock", "あとから増えたプロファイル"));
        }

        // ── 切り替え ─────────────────────────────────────────────────────

        [Fact]
        public void PerProfile_IsIndependent()
        {
            ExtensionStateStore.SetEnabled(_states, "uBlock", "profile-a", false);
            ExtensionStateStore.SetEnabled(_states, "uBlock", "profile-b", true);

            Assert.False(ExtensionStateStore.IsEnabled(_states, "uBlock", "profile-a"));
            Assert.True(ExtensionStateStore.IsEnabled(_states, "uBlock", "profile-b"));
        }

        [Fact]
        public void PerProfile_WinsOverTheDefault()
        {
            // 明示的に切り替えたものが既定に負けては、切り替えた意味が無い
            ExtensionStateStore.SetEnabledByDefault(_states, "uBlock", false);
            ExtensionStateStore.SetEnabled(_states, "uBlock", "profile-a", true);

            Assert.True(ExtensionStateStore.IsEnabled(_states, "uBlock", "profile-a"));
        }

        [Fact]
        public void ChangingTheDefault_DoesNotTouchExistingProfiles()
        {
            ExtensionStateStore.SetEnabled(_states, "uBlock", "profile-a", true);
            ExtensionStateStore.SetEnabledByDefault(_states, "uBlock", false);

            Assert.True(ExtensionStateStore.IsEnabled(_states, "uBlock", "profile-a"));
        }

        [Fact]
        public void ExtensionsAreIndependentOfEachOther()
        {
            ExtensionStateStore.SetEnabled(_states, "uBlock", "profile-a", false);

            Assert.True(ExtensionStateStore.IsEnabled(_states, "Stylus", "profile-a"));
        }

        // ── 鍵 ───────────────────────────────────────────────────────────

        [Theory]
        [InlineData(@"C:\data\extensions\uBlock", "uBlock")]
        [InlineData(@"C:\data\extensions\uBlock\", "uBlock")]
        public void KeyOf_UsesTheFolderName(string dir, string expected)
            => Assert.Equal(expected, ExtensionStateStore.KeyOf(dir));

        // ── 後始末 ───────────────────────────────────────────────────────

        [Fact]
        public void Forget_DropsTheRecord()
        {
            ExtensionStateStore.SetEnabled(_states, "uBlock", "profile-a", false);
            ExtensionStateStore.Forget(_states, "uBlock");

            // 記録が無い＝既定（有効）に戻る
            Assert.True(ExtensionStateStore.IsEnabled(_states, "uBlock", "profile-a"));
        }

        [Fact]
        public void Prune_DropsExtensionsThatAreGone()
        {
            // 放っておくと、同じ名前で入れ直したときに昔の設定が蘇る
            ExtensionStateStore.SetEnabled(_states, "Removed", "profile-a", false);
            ExtensionStateStore.SetEnabled(_states, "Kept",    "profile-a", false);

            ExtensionStateStore.Prune(_states, ["Kept"], ["profile-a"]);

            Assert.True(ExtensionStateStore.IsEnabled(_states, "Removed", "profile-a"));
            Assert.False(ExtensionStateStore.IsEnabled(_states, "Kept", "profile-a"));
        }

        [Fact]
        public void Prune_DropsProfilesThatAreGone()
        {
            ExtensionStateStore.SetEnabled(_states, "uBlock", "生きている", false);
            ExtensionStateStore.SetEnabled(_states, "uBlock", "消えた",     false);

            ExtensionStateStore.Prune(_states, ["uBlock"], ["生きている"]);

            Assert.False(ExtensionStateStore.IsEnabled(_states, "uBlock", "生きている"));
            Assert.True(ExtensionStateStore.IsEnabled(_states, "uBlock", "消えた"));
        }

        [Fact]
        public void Prune_KeepsTheExtensionDefault()
        {
            // プロファイルが 1 つ消えただけで、拡張機能そのものの既定まで捨てない
            ExtensionStateStore.SetEnabledByDefault(_states, "uBlock", false);
            ExtensionStateStore.SetEnabled(_states, "uBlock", "消えた", true);

            ExtensionStateStore.Prune(_states, ["uBlock"], ["生きている"]);

            Assert.False(ExtensionStateStore.IsEnabled(_states, "uBlock", "生きている"));
        }

        [Fact]
        public void Prune_NothingToDo_ReturnsZero()
        {
            ExtensionStateStore.SetEnabled(_states, "uBlock", "profile-a", false);
            Assert.Equal(0, ExtensionStateStore.Prune(_states, ["uBlock"], ["profile-a"]));
        }

        // ── フォルダーの削除 ─────────────────────────────────────────────

        [Fact]
        public void DeleteFolder_RemovesEverything()
        {
            var ext = Path.Combine(_dir, "uBlock");
            Directory.CreateDirectory(Path.Combine(ext, "_locales", "ja"));
            File.WriteAllText(Path.Combine(ext, "manifest.json"), "{}");
            File.WriteAllText(Path.Combine(ext, "_locales", "ja", "messages.json"), "{}");

            Assert.True(ExtensionStateStore.DeleteFolder(ext));
            Assert.False(Directory.Exists(ext));
        }

        [Fact]
        public void DeleteFolder_Missing_IsNotAFailure()
            => Assert.True(ExtensionStateStore.DeleteFolder(Path.Combine(_dir, "no-such-ext")));

        [Fact]
        public void DeleteFolder_Locked_ReportsFailureWithoutThrowing()
        {
            // 消せなかったことを呼び出し側が知る必要がある。
            // 「消えた」と思って記録まで捨てると、残ったフォルダーが次の起動で復活する。
            var ext = Path.Combine(_dir, "Locked");
            Directory.CreateDirectory(ext);
            var f = Path.Combine(ext, "manifest.json");
            File.WriteAllText(f, "{}");

            using var hold = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None);

            Assert.False(ExtensionStateStore.DeleteFolder(ext));
            Assert.True(Directory.Exists(ext));
        }
    }
}
