using XTimelineViewer.Models;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// ハンドル依存タイムラインの URL 組み立てと種別判定（#441）。
    ///
    /// いいね・プロフィール・メディア・リストは <c>x.com/&lt;handle&gt;…</c> の
    /// アカウント依存 URL。委任アカウント切り替えに追従させるため、作成時の
    /// ハンドルを焼き込まずパスだけ保持し、ライブ解決したハンドルで組み立て直す。
    /// 種別ごとに bool を増やす代わりにここへ一本化したので、組み立てと
    /// 「既知の種別か」の判定が崩れないことを固定する。
    /// </summary>
    public class HandleTimelineTests
    {
        [Theory]
        [InlineData(HandleTimeline.Lists,   "https://x.com/daru/lists")]
        [InlineData(HandleTimeline.Likes,   "https://x.com/daru/likes")]
        [InlineData(HandleTimeline.Media,   "https://x.com/daru/media")]
        [InlineData(HandleTimeline.Profile, "https://x.com/daru")]   // プロフィールはハンドルそのもの
        public void BuildUrl_AppendsThePath(string path, string expected)
            => Assert.Equal(expected, HandleTimeline.BuildUrl("daru", path));

        [Theory]
        [InlineData(HandleTimeline.Lists)]
        [InlineData(HandleTimeline.Likes)]
        [InlineData(HandleTimeline.Media)]
        [InlineData(HandleTimeline.Profile)]
        public void IsKnown_AcceptsEveryDefinedKind(string path)
            => Assert.True(HandleTimeline.IsKnown(path));

        [Fact]
        public void IsKnown_RejectsUnknownOrNull()
        {
            // 壊れた保存値で x.com/<handle>/妙なもの を組み立てて飛ばないための歯止め。
            Assert.False(HandleTimeline.IsKnown(null));
            Assert.False(HandleTimeline.IsKnown("/bookmarks"));
            Assert.False(HandleTimeline.IsKnown("/likes/extra"));
            Assert.False(HandleTimeline.IsKnown("likes"));   // 先頭スラッシュ無し
        }

        [Fact]
        public void Profile_IsAnEmptyPath_NotNull()
        {
            // プロフィールは「相対パスが空」であって「ハンドル依存でない(null)」とは違う。
            // ここを取り違えると、プロフィールのライブ解決が働かなくなる。
            Assert.Equal("", HandleTimeline.Profile);
            Assert.True(HandleTimeline.IsKnown(HandleTimeline.Profile));
        }

        // ── 旧 IsListsIndex からの移行（#441） ──────────────────────────────

        [Fact]
        public void LegacyListsIndex_MigratesToHandlePath()
        {
            // 既存の timelines.json は IsListsIndex=true を持つ。復元時に
            // HandlePath="/lists" へ移す。ここが崩れると、既存ユーザーの
            // リスト一覧がアカウント追従を失う。
            var cfg = new TimelineConfig { IsListsIndex = true };

            HandleTimeline.MigrateLegacy(cfg);

            Assert.Equal(HandleTimeline.Lists, cfg.HandlePath);
        }

        [Fact]
        public void NewHandlePath_IsNotOverwrittenByLegacyMigration()
        {
            // HandlePath を持つ新しい設定は、旧フラグに触れず尊重する。
            var cfg = new TimelineConfig { HandlePath = HandleTimeline.Likes, IsListsIndex = true };

            HandleTimeline.MigrateLegacy(cfg);

            Assert.Equal(HandleTimeline.Likes, cfg.HandlePath);
        }

        [Fact]
        public void NonHandleTimeline_IsLeftAlone()
        {
            // 通常のタイムライン（旧フラグも無い）は HandlePath=null のまま。
            var cfg = new TimelineConfig { Url = "https://x.com/home" };

            HandleTimeline.MigrateLegacy(cfg);

            Assert.Null(cfg.HandlePath);
        }
    }
}
