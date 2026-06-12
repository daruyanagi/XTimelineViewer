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
    }
}
