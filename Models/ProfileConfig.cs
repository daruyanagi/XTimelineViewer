using System;

namespace XTimelineViewer.Models
{
    public class ProfileConfig
    {
        public string  Id             { get; set; } = Guid.NewGuid().ToString("N");
        public string  Name           { get; set; } = "";
        public int?    BadgeColorIndex{ get; set; }
        public string? BadgeText      { get; set; }

        /// <summary>
        /// X のスクリーンネーム（@ 以降のハンドル）。Name はユーザーが自由に変更できるため、
        /// ハンドル依存の URL（リスト一覧など）の組み立てにはこちらを使う。
        /// 作成時に検出し、ペイン読み込み時にも補完する。
        /// </summary>
        public string? ScreenName     { get; set; }
    }
}
