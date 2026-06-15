using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services;

public class UrlHelperTests
{
    // ── IsXUrl ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://x.com/home",           true)]
    [InlineData("https://twitter.com/home",     true)]
    [InlineData("https://X.COM/home",           true)]
    [InlineData("https://example.com/",         false)]
    [InlineData("https://nitter.net/home",      false)]
    public void IsXUrl_Works(string url, bool expected)
        => Assert.Equal(expected, UrlHelper.IsXUrl(url));

    // ── IsOnBaseUrl ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://x.com/home",          "https://x.com/home",  true)]
    [InlineData("https://x.com/home?foo=bar",  "https://x.com/home",  true)]  // クエリは無視
    [InlineData("https://X.com/HOME",          "https://x.com/home",  true)]  // 大文字小文字を無視
    [InlineData("https://x.com/notifications", "https://x.com/home",  false)]
    [InlineData("https://twitter.com/home",    "https://x.com/home",  false)] // ホストが異なる
    [InlineData("not-a-url",                   "https://x.com/home",  false)]
    [InlineData("https://x.com/home",          "not-a-url",           false)]
    public void IsOnBaseUrl_Works(string current, string baseUrl, bool expected)
        => Assert.Equal(expected, UrlHelper.IsOnBaseUrl(current, baseUrl));

    // ── GetTimelineGlyph ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://x.com/home",              "\uE80F")] // Home
    [InlineData("https://x.com/notifications",     "\uE7E7")] // Bell
    [InlineData("https://x.com/search?q=test",     "\uE71E")] // Search
    [InlineData("https://x.com/explore",           "\uE71E")] // Search
    [InlineData("https://x.com/i/bookmarks",       "\uE734")] // Bookmark
    [InlineData("https://x.com/daruyanagi/lists",  "\uE71D")] // BulletedList (per-user lists index)
    [InlineData("https://x.com/i/lists/123",       "\uE71D")] // BulletedList (individual list)
    [InlineData("https://x.com/messages",          "\uE8BD")] // Chat
    [InlineData("https://x.com/daruyanagi",        "\uE77B")] // Contact
    [InlineData("https://x.com/i/grok",            "\uE774")] // Globe (fallback)
    [InlineData("not-a-url",                       "")]
    public void GetTimelineGlyph_Works(string url, string expected)
        => Assert.Equal(expected, UrlHelper.GetTimelineGlyph(url));

    // ── IsListHeaderApplicable ────────────────────────────────────────────────

    [Theory]
    [InlineData("https://x.com/notifications",  true)]
    [InlineData("https://x.com/search?q=test",  true)]
    [InlineData("https://x.com/explore",        true)]
    [InlineData("https://x.com/i/bookmarks",    true)]
    [InlineData("https://x.com/i/lists/123",    true)]
    [InlineData("https://x.com/daruyanagi",     true)]  // プロファイルページ
    [InlineData("https://x.com/home",           false)]
    [InlineData("https://x.com/messages",       false)]
    [InlineData("https://x.com/i/grok",         false)]
    [InlineData("not-a-url",                    false)]
    public void IsListHeaderApplicable_Works(string url, bool expected)
        => Assert.Equal(expected, UrlHelper.IsListHeaderApplicable(url));

    // ── IsPerUserListsUrl ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://x.com/daruyanagi/lists", true)]
    [InlineData("https://x.com/i/lists",          true)]   // /<seg>/lists にマッチ
    [InlineData("https://x.com/i/lists/123",      false)]  // 個別リストは対象外
    [InlineData("https://x.com/daruyanagi",       false)]  // プロフィール
    [InlineData("https://x.com/home",             false)]
    [InlineData("not-a-url",                      false)]
    public void IsPerUserListsUrl_Works(string url, bool expected)
        => Assert.Equal(expected, UrlHelper.IsPerUserListsUrl(url));

    // ── ParseUrlShortcut ──────────────────────────────────────────────────────

    [Fact]
    public void ParseUrlShortcut_ExtractsUrl()
    {
        string[] lines = ["[InternetShortcut]", "URL=https://x.com/home", "IconIndex=0"];
        Assert.Equal("https://x.com/home", UrlHelper.ParseUrlShortcut(lines));
    }

    [Fact]
    public void ParseUrlShortcut_CaseInsensitiveAndTrimmed()
    {
        string[] lines = ["url=https://x.com/home  "];
        Assert.Equal("https://x.com/home", UrlHelper.ParseUrlShortcut(lines));
    }

    [Fact]
    public void ParseUrlShortcut_NoUrlLine_ReturnsNull()
    {
        string[] lines = ["[InternetShortcut]", "IconIndex=0"];
        Assert.Null(UrlHelper.ParseUrlShortcut(lines));
    }
}
