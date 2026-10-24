namespace XTimelineViewer.Models
{
    internal class TimelineConfig
    {
        public string Url                { get; set; } = "";
        public double Width              { get; set; } = 350;
        public bool   HideSidebar       { get; set; } = false;
        public bool   HideCompose       { get; set; } = true;
        public bool   HideListHeader    { get; set; } = false;
        public bool   HardReloadEnabled { get; set; } = false;
        public int    HardReloadInterval{ get; set; } = 3;
        public string ProfileId         { get; set; } = "default";

        /// <summary>
        /// ハンドル依存タイムラインのハンドル相対パス（#441）。null は非ハンドル依存。
        /// "/lists" / "/likes" / "/media" / ""（プロフィール）。委任アカウントを
        /// 考慮し、作成時のキャッシュではなくペイン読み込みごとにライブ解決した
        /// ハンドルで URL を組み立て直す（#211 の仕組みを一般化）。
        /// </summary>
        public string? HandlePath       { get; set; }

        /// <summary>
        /// 旧「リスト一覧」フラグ（#211）。<see cref="HandlePath"/> へ一般化した（#441）。
        /// 既存の timelines.json を読むためだけに残す。復元時に移行する。
        /// </summary>
        public bool   IsListsIndex      { get; set; } = false;
    }
}
