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
        /// 「リスト」メニューから追加した、アクティブアカウントのリスト一覧を追跡するタイムライン。
        /// X の委任アカウントを考慮し、ハンドルは作成時のキャッシュではなくペイン読み込みごとに
        /// ライブ取得して URL（https://x.com/&lt;active&gt;/lists）を解決する (#211)。
        /// </summary>
        public bool   IsListsIndex      { get; set; } = false;
    }
}
