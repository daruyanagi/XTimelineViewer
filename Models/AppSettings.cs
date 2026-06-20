using System.Collections.Generic;

namespace XTimelineViewer.Models
{
    public class AppSettings
    {
        public bool    OpenComposerInBrowser { get; set; } = false;
        public bool    OpenTimestampInBrowser{ get; set; } = false;
        public string  Theme                 { get; set; } = "Default"; // "Light" | "Dark" | "Default"
        public int     AutoActivateMinutes   { get; set; } = 0;
        public string  Language              { get; set; } = "system";  // "system" | "ja-JP" | "en-US"
        public string? CachedLatestVersion   { get; set; } = null;      // "v1.4.0" など
        public bool    DefaultHideSidebar   { get; set; } = false;     // 新規タイムラインの既定値
        public bool    DefaultHideCompose   { get; set; } = true;      // 新規タイムラインの既定値
        public bool    DefaultHideListHeader{ get; set; } = false;     // 新規タイムラインの既定値
        public bool    ShowAutoActivateLabel { get; set; } = false;
        public string  ExternalBrowser       { get; set; } = "system";  // "system" | "edge"
        public string  EdgeProfileDirectory  { get; set; } = "";        // "Default" | "Profile 1" など
        public string? LastUsedProfileId     { get; set; } = null;      // 投稿画面で最後に使ったプロファイル
        public List<string> SavedSearchQueries { get; set; } = [];        // 検索ボックスのサジェスト用
        public bool    HomeAutoLoadEnabled   { get; set; } = true;       // ホーム自動更新（#207）の ON/OFF
        public int     HomeAutoLoadIntervalSeconds { get; set; } = 8;    // ホーム自動更新の間隔（秒, 最小 5）
    }
}
