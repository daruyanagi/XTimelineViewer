using System;
using System.Collections.Generic;
using System.Linq;
using XTimelineViewer.Models;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// 拡張機能の更新をいつ調べるか（#432）。
    ///
    /// 拡張機能ページを開いたときに自動で調べる。ただし<b>開くたびには調べない</b>。
    /// GitHub の API は認証なしだと <b>1 時間 60 回</b>（実測）で、拡張機能 1 つにつき
    /// 1 回叩く。しかもアプリ本体の更新チェックが同じ枠を使う。拡張機能を 10 個
    /// 入れている人が設定を開き直すと 6 回で枯れる。
    ///
    /// アプリ本体の更新チェックと同じく 24 時間おきにする（#328）。
    /// 揃えないと「どちらがどの間隔だったか」を覚える羽目になる。
    /// </summary>
    internal static class ExtensionUpdateCheck
    {
        internal static readonly TimeSpan Interval = TimeSpan.FromHours(24);

        /// <summary>
        /// この拡張機能を今調べるべきか。
        ///
        /// 入手先を知らないものは調べようがないので false。
        /// </summary>
        internal static bool IsDue(ExtensionState? state, DateTimeOffset now)
        {
            if (state is null) return false;
            if (string.IsNullOrWhiteSpace(state.SourceRepoUrl)) return false;
            if (state.LastUpdateCheck is not { } last) return true;

            // 時計が巻き戻った場合（時刻合わせ・タイムゾーン変更）。
            // 素直に now - last >= Interval とだけ書くと、未来の記録が残って
            // 「その時刻から 24 時間」調べなくなる。
            if (last > now) return true;

            return now - last >= Interval;
        }

        /// <summary>調べるべきものだけを選ぶ。順序は渡された通り。</summary>
        internal static IReadOnlyList<string> DueKeys(
            IReadOnlyDictionary<string, ExtensionState> states,
            IEnumerable<string> keys,
            DateTimeOffset now)
            => keys.Where(k => IsDue(states.TryGetValue(k, out var st) ? st : null, now))
                   .ToList();

        /// <summary>前回調べた結果。</summary>
        internal enum Cached
        {
            /// <summary>まだ一度も調べていない。何も出さない。</summary>
            Unknown,
            /// <summary>調べて、最新だった。</summary>
            UpToDate,
            /// <summary>調べて、新しい版があった。</summary>
            UpdateAvailable,
        }

        /// <summary>
        /// 前回調べた結果を返す。
        ///
        /// <b>「調べていない」と「調べて最新だった」を区別する。</b>
        /// タグの有無だけで表すと、最新だったときに何も出せず、
        /// 利用者からは「調べていない」のと見分けがつかない。
        ///
        /// <paramref name="installedVersion"/> と突き合わせるのは、
        /// <b>調べたあとに手で入れ替えた</b>場合に古い「更新があります」を
        /// 出し続けないため。
        /// </summary>
        internal static (Cached State, string? Tag) CachedResult(
            ExtensionState? state, string? installedVersion)
        {
            if (state?.LastUpdateCheck is null) return (Cached.Unknown, null);
            if (string.IsNullOrWhiteSpace(state.CachedUpdateTag)) return (Cached.UpToDate, null);

            return ExtensionUpdater.IsNewer(installedVersion, state.CachedUpdateTag)
                ? (Cached.UpdateAvailable, state.CachedUpdateTag)
                : (Cached.UpToDate, null);
        }

        /// <summary>調べた結果を記録する。<paramref name="tag"/> が null なら「最新だった」。</summary>
        internal static void Record(
            Dictionary<string, ExtensionState> states, string key, string? tag, DateTimeOffset now)
        {
            if (!states.TryGetValue(key, out var st))
            {
                st = new ExtensionState();
                states[key] = st;
            }
            st.LastUpdateCheck  = now;
            st.CachedUpdateTag  = tag;
        }
    }
}
