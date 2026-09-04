namespace XTimelineViewer.Services
{
    /// <summary>
    /// 配布元の URL。
    ///
    /// 同じ URL をバージョン情報ページ・更新チェック・メニューの 3 箇所で
    /// 手書きしていた（#382）。リポジトリを移したときに直し漏れる形なのでまとめる。
    /// </summary>
    internal static class AppUrls
    {
        internal const string Repo = "https://github.com/daruyanagi/XTimelineViewer";

        /// <summary>リリース一覧（ユーザーに見せるページ）。</summary>
        internal const string LatestRelease = Repo + "/releases/latest";

        /// <summary>フィードバック（新規 issue）の投稿先（#426）。</summary>
        internal const string NewIssue = Repo + "/issues/new";

        /// <summary>最新リリースの取得に使う API。</summary>
        internal const string LatestReleaseApi =
            "https://api.github.com/repos/daruyanagi/XTimelineViewer/releases/latest";
    }
}
