using System;
using System.Collections.Generic;
using XTimelineViewer.Models;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// 拡張機能の更新をいつ調べるか（#432）。
    ///
    /// <b>ここが緩むと GitHub の枠を食い潰す。</b> 認証なしの API は
    /// 1 時間 60 回（実測）で、拡張機能 1 つにつき 1 回叩く。アプリ本体の
    /// 更新チェックも同じ枠を使うので、設定を開き直すだけで枯れる。
    /// #406 が自動チェックを見送ったのはこれが理由で、24 時間ぶん
    /// キャッシュすることだけが前提を変えている。
    /// </summary>
    public class ExtensionUpdateCheckTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(9));

        private static ExtensionState WithSource(DateTimeOffset? last = null, string? cached = null)
            => new()
            {
                SourceRepoUrl   = "https://github.com/o/r",
                LastUpdateCheck = last,
                CachedUpdateTag = cached,
            };

        // ── いつ調べるか ─────────────────────────────────────────────────

        [Fact]
        public void NeverChecked_IsDue()
            => Assert.True(ExtensionUpdateCheck.IsDue(WithSource(), Now));

        [Fact]
        public void JustChecked_IsNotDue()
        {
            // 開き直すたびに叩かないこと。#432 の肝。
            Assert.False(ExtensionUpdateCheck.IsDue(WithSource(Now.AddMinutes(-1)), Now));
            Assert.False(ExtensionUpdateCheck.IsDue(WithSource(Now.AddHours(-23)), Now));
        }

        [Fact]
        public void AfterTheInterval_IsDue()
        {
            Assert.True(ExtensionUpdateCheck.IsDue(WithSource(Now - ExtensionUpdateCheck.Interval), Now));
            Assert.True(ExtensionUpdateCheck.IsDue(WithSource(Now.AddDays(-3)), Now));
        }

        [Fact]
        public void TheIntervalMatchesTheAppsOwnCheck()
        {
            // アプリ本体の更新チェックと揃えてある（#328）。
            // 片方だけ変えると「どちらがどの間隔か」を覚える羽目になる。
            Assert.Equal(TimeSpan.FromHours(24), ExtensionUpdateCheck.Interval);
        }

        [Fact]
        public void ClockMovedBackwards_IsDue()
        {
            // 時刻合わせやタイムゾーン変更で未来の記録が残ることがある。
            // now - last >= Interval とだけ書くと、その時刻から 24 時間
            // 調べなくなる（巻き戻し幅によっては何日も）。
            Assert.True(ExtensionUpdateCheck.IsDue(WithSource(Now.AddDays(1)), Now));
        }

        [Fact]
        public void WithoutASource_IsNeverDue()
        {
            // 手で置いたものは入手先が分からない。叩く先が無い。
            Assert.False(ExtensionUpdateCheck.IsDue(new ExtensionState(), Now));
            Assert.False(ExtensionUpdateCheck.IsDue(new ExtensionState { SourceRepoUrl = "  " }, Now));
            Assert.False(ExtensionUpdateCheck.IsDue(null, Now));
        }

        [Fact]
        public void DueKeys_PicksOnlyTheExpiredOnes()
        {
            var states = new Dictionary<string, ExtensionState>
            {
                ["old"]      = WithSource(Now.AddDays(-2)),
                ["fresh"]    = WithSource(Now.AddHours(-1)),
                ["never"]    = WithSource(),
                ["no-source"] = new ExtensionState(),
            };

            var due = ExtensionUpdateCheck.DueKeys(
                states, ["old", "fresh", "never", "no-source", "unknown"], Now);

            Assert.Equal(["old", "never"], due);
        }

        // ── 前回の結果 ───────────────────────────────────────────────────

        [Fact]
        public void Cached_UpdateAvailable_KeepsTheTag()
            => Assert.Equal((ExtensionUpdateCheck.Cached.UpdateAvailable, "v2.0.0"),
                ExtensionUpdateCheck.CachedResult(WithSource(Now, "v2.0.0"), "1.0.0"));

        [Fact]
        public void Cached_UpToDate_IsDistinctFromNotChecked()
        {
            // ここを一緒くたにすると「最新です」が出せない。実際に出なかった。
            // タグの有無だけで表すと、最新だったときに何も表示できず、
            // 利用者からは調べていないのと見分けがつかない。
            Assert.Equal(ExtensionUpdateCheck.Cached.UpToDate,
                ExtensionUpdateCheck.CachedResult(WithSource(Now), "1.0.0").State);

            Assert.Equal(ExtensionUpdateCheck.Cached.Unknown,
                ExtensionUpdateCheck.CachedResult(WithSource(), "1.0.0").State);

            Assert.Equal(ExtensionUpdateCheck.Cached.Unknown,
                ExtensionUpdateCheck.CachedResult(null, "1.0.0").State);
        }

        [Fact]
        public void Cached_UpdateIsDroppedOnceInstalled()
        {
            // 調べたあとに手で入れ替えた場合。古い「更新があります」を出し続けない。
            // 調べたことは確かなので、Unknown ではなく UpToDate に落とす。
            Assert.Equal((ExtensionUpdateCheck.Cached.UpToDate, (string?)null),
                ExtensionUpdateCheck.CachedResult(WithSource(Now, "v2.0.0"), "2.0.0"));
            Assert.Equal((ExtensionUpdateCheck.Cached.UpToDate, (string?)null),
                ExtensionUpdateCheck.CachedResult(WithSource(Now, "v2.0.0"), "2.1.0"));
        }

        // ── 記録 ─────────────────────────────────────────────────────────

        [Fact]
        public void Record_StampsTheTime_SoTheNextOpenDoesNotHitTheApi()
        {
            var states = new Dictionary<string, ExtensionState> { ["k"] = WithSource() };

            ExtensionUpdateCheck.Record(states, "k", "v2.0.0", Now);

            Assert.Equal(Now, states["k"].LastUpdateCheck);
            Assert.Equal("v2.0.0", states["k"].CachedUpdateTag);
            Assert.False(ExtensionUpdateCheck.IsDue(states["k"], Now));
        }

        [Fact]
        public void Record_UpToDate_AlsoCounts()
        {
            // 「最新だった」も記録する。でないと最新のものだけ毎回叩く。
            var states = new Dictionary<string, ExtensionState> { ["k"] = WithSource(null, "v9.9.9") };

            ExtensionUpdateCheck.Record(states, "k", null, Now);

            Assert.Null(states["k"].CachedUpdateTag);
            Assert.False(ExtensionUpdateCheck.IsDue(states["k"], Now));
        }

        [Fact]
        public void Record_KeepsTheSource()
        {
            // 入手先を消すと、次から調べる対象ですらなくなる。
            var states = new Dictionary<string, ExtensionState> { ["k"] = WithSource() };
            ExtensionUpdateCheck.Record(states, "k", null, Now);
            Assert.Equal("https://github.com/o/r", states["k"].SourceRepoUrl);
        }
    }
}
