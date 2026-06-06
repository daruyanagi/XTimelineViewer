using System;

namespace XTimelineViewer.Models
{
    internal record ExtensionInfo(
        string Name,
        string DirectoryPath,
        string? IconPath,
        string? OptionsPage,
        string? HomepageUrl,
        string? ExtensionId
    );

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
    }

    public class ProfileConfig
    {
        public string  Id             { get; set; } = Guid.NewGuid().ToString("N");
        public string  Name           { get; set; } = "";
        public int?    BadgeColorIndex{ get; set; }
        public string? BadgeText      { get; set; }
    }

    public class AppSettings
    {
        public bool    SeparateComposeEnv    { get; set; } = false;
        public bool    OpenComposerInBrowser { get; set; } = false;
        public bool    OpenTimestampInBrowser{ get; set; } = false;
        public string  Theme                 { get; set; } = "Default"; // "Light" | "Dark" | "Default"
        public int     AutoActivateMinutes   { get; set; } = 0;
        public string  Language              { get; set; } = "system";  // "system" | "ja-JP" | "en-US"
        public string? LastUpdateCheck       { get; set; } = null;      // UTC ISO-8601
        public string? CachedLatestVersion   { get; set; } = null;      // "v1.4.0" など
        public bool    ShowAutoActivateLabel { get; set; } = false;
        public string  ExternalBrowser       { get; set; } = "system";  // "system" | "edge"
        public string  EdgeProfileDirectory  { get; set; } = "";        // "Default" | "Profile 1" など
    }
}
