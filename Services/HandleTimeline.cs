using XTimelineViewer.Models;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// ハンドル依存タイムラインの種別と URL 組み立て（#441）。
    ///
    /// X のいくつかのページは <c>https://x.com/&lt;handle&gt;/…</c> という
    /// アカウントのハンドルに紐づく URL を持つ。委任アカウントの切り替えに
    /// 追従させたいので、作成時のハンドルを焼き込まず、ペイン読み込みごとに
    /// ライブ解決した<b>ハンドル相対パス</b>で URL を組み立て直す。
    ///
    /// 以前はリスト一覧だけが <c>TimelineConfig.IsListsIndex</c>（bool）で
    /// この扱いを受けていた。いいね・プロフィール・メディアが加わって 4 種に
    /// なったので、種別ごとに bool を増やすのをやめ、ここへ一本化した。
    /// </summary>
    internal static class HandleTimeline
    {
        /// <summary>リスト一覧（<c>/&lt;handle&gt;/lists</c>）。</summary>
        internal const string Lists = "/lists";

        /// <summary>いいね（<c>/&lt;handle&gt;/likes</c>）。自分がいいねした投稿。</summary>
        internal const string Likes = "/likes";

        /// <summary>メディア（<c>/&lt;handle&gt;/media</c>）。自分が上げた画像・動画。</summary>
        internal const string Media = "/media";

        /// <summary>プロフィール（<c>/&lt;handle&gt;</c>）。ハンドルそのもの＝相対パスは空。</summary>
        internal const string Profile = "";

        private static readonly string[] Known = [Lists, Likes, Media, Profile];

        /// <summary>
        /// 保存値が既知のハンドル相対パスか。
        /// 壊れた・未知の値で <c>https://x.com/&lt;handle&gt;/妙なもの</c> を
        /// 組み立てて飛ばないように、ライブ解決の前に確かめる。
        /// </summary>
        internal static bool IsKnown(string? handlePath)
            => handlePath is not null && System.Array.IndexOf(Known, handlePath) >= 0;

        /// <summary>
        /// ハンドルと相対パスから URL を組み立てる。
        /// プロフィール（相対パスが空）は <c>https://x.com/&lt;handle&gt;</c> になる。
        /// </summary>
        internal static string BuildUrl(string handle, string handlePath)
            => $"https://x.com/{handle}{handlePath}";

        /// <summary>
        /// 旧形式の設定を現行の <see cref="TimelineConfig.HandlePath"/> へ移行する（#441）。
        /// 以前はリスト一覧だけが <c>IsListsIndex</c>（#211）で追従していた。
        /// 復元時に呼ぶ。新しい HandlePath を持つ設定には触れない。
        /// </summary>
        internal static void MigrateLegacy(TimelineConfig cfg)
        {
            if (cfg.HandlePath is null && cfg.IsListsIndex)
                cfg.HandlePath = Lists;
        }
    }
}
