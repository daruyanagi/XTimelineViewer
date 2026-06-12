using System;

namespace XTimelineViewer.Models
{
    public class ProfileConfig
    {
        public string  Id             { get; set; } = Guid.NewGuid().ToString("N");
        public string  Name           { get; set; } = "";
        public int?    BadgeColorIndex{ get; set; }
        public string? BadgeText      { get; set; }
    }
}
