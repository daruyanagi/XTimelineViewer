using System.Collections.Generic;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services;

public class SearchQueryHelperTests
{
    // ── BuildSearchUrl ────────────────────────────────────────────────────────

    [Fact]
    public void BuildSearchUrl_EncodesQuery()
        => Assert.Equal(
            "https://x.com/search?q=%E6%97%A5%E6%9C%AC%E4%BB%A3%E8%A1%A8&src=typed_query",
            SearchQueryHelper.BuildSearchUrl("日本代表"));

    [Fact]
    public void BuildSearchUrl_EncodesSpecialChars()
        => Assert.Equal(
            "https://x.com/search?q=Ohtani%20AND%20MLB&src=typed_query",
            SearchQueryHelper.BuildSearchUrl("Ohtani AND MLB"));

    // ── ExtractQueryFromUrl ───────────────────────────────────────────────────

    [Theory]
    [InlineData("https://x.com/search?q=test&src=typed_query", "test")]
    [InlineData("https://x.com/search?q=%E6%97%A5%E6%9C%AC",   "日本")]
    [InlineData("https://x.com/search?f=live&q=test",          "test")]
    [InlineData("https://x.com/home",                          null)]
    [InlineData("not-a-url",                                   null)]
    [InlineData(null,                                          null)]
    public void ExtractQueryFromUrl_Works(string? url, string? expected)
        => Assert.Equal(expected, SearchQueryHelper.ExtractQueryFromUrl(url));

    // ── ExtractSearchPath ─────────────────────────────────────────────────────

    [Fact]
    public void ExtractSearchPath_RemovesSrcParam()
        => Assert.Equal(
            "/search?q=test",
            SearchQueryHelper.ExtractSearchPath("https://x.com/search?q=test&src=typed_query"));

    [Fact]
    public void ExtractSearchPath_KeepsFilterParam()
        => Assert.Equal(
            "/search?q=test&f=live",
            SearchQueryHelper.ExtractSearchPath("https://x.com/search?q=test&f=live&src=typed_query"));

    [Theory]
    [InlineData("https://x.com/home")]
    [InlineData("https://x.com/search")]
    [InlineData("not-a-url")]
    [InlineData(null)]
    public void ExtractSearchPath_InvalidInput_ReturnsNull(string? url)
        => Assert.Null(SearchQueryHelper.ExtractSearchPath(url));

    // ── DecodeSearchPath ──────────────────────────────────────────────────────

    [Fact]
    public void DecodeSearchPath_DecodesJapanese()
        => Assert.Equal(
            "/search?q=日本代表&f=live",
            SearchQueryHelper.DecodeSearchPath("/search?q=%E6%97%A5%E6%9C%AC%E4%BB%A3%E8%A1%A8&f=live"));

    // ── ExtractQueryFromSearchPath ────────────────────────────────────────────

    [Fact]
    public void ExtractQueryFromSearchPath_Works()
        => Assert.Equal(
            "日本代表",
            SearchQueryHelper.ExtractQueryFromSearchPath("/search?q=%E6%97%A5%E6%9C%AC%E4%BB%A3%E8%A1%A8&f=live"));

    // ── AddSavedQuery（重複排除・先頭挿入） ───────────────────────────────────

    [Fact]
    public void AddSavedQuery_NewQuery_InsertsAtHead()
    {
        var saved = new List<string> { "/search?q=old" };

        SearchQueryHelper.AddSavedQuery(saved, "/search?q=new");

        Assert.Equal(["/search?q=new", "/search?q=old"], saved);
    }

    [Fact]
    public void AddSavedQuery_ExistingQuery_MovesToHeadWithoutDuplicate()
    {
        var saved = new List<string> { "/search?q=a", "/search?q=b", "/search?q=c" };

        SearchQueryHelper.AddSavedQuery(saved, "/search?q=b");

        Assert.Equal(["/search?q=b", "/search?q=a", "/search?q=c"], saved);
    }

    [Fact]
    public void AddSavedQuery_EmptyList_Works()
    {
        var saved = new List<string>();

        SearchQueryHelper.AddSavedQuery(saved, "/search?q=first");

        Assert.Equal(["/search?q=first"], saved);
    }

    // ── 往復（Build → Extract） ───────────────────────────────────────────────

    [Theory]
    [InlineData("日本代表")]
    [InlineData("Ohtani AND MLB")]
    [InlineData("#ハッシュタグ from:user")]
    public void BuildAndExtract_RoundTrip(string query)
        => Assert.Equal(query, SearchQueryHelper.ExtractQueryFromUrl(SearchQueryHelper.BuildSearchUrl(query)));
}
