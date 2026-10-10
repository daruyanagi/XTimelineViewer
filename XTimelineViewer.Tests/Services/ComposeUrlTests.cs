using System;
using System.Web;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// 投稿画面の URL（#431）。
    ///
    /// <b>共有されるのは記事の URL がほとんど</b>で、<c>?</c> <c>&amp;</c> <c>#</c> を
    /// 含むのが普通。素で繋ぐとそこから先が落ち、利用者には
    /// 「本文が途中で切れている」形に見える。#426 で同じ踏み方をしている。
    /// </summary>
    public class ComposeUrlTests
    {
        [Fact]
        public void NoText_IsThePlainComposeUrl()
        {
            Assert.Equal(ComposeUrl.Base, ComposeUrl.For(null));
            Assert.Equal(ComposeUrl.Base, ComposeUrl.For("   "));
        }

        [Theory]
        [InlineData("ただの文章")]
        [InlineData("https://example.com/a?b=1&c=2")]          // & で切れる
        [InlineData("https://example.com/a#section")]           // # から先が fragment になる
        [InlineData("記事の表題\nhttps://example.com/a?x=1")]   // 改行つき（ShareDraft の出力）
        [InlineData("100% 完了")]
        [InlineData("a+b=c")]
        public void Text_SurvivesTheRoundTrip(string text)
        {
            var url = ComposeUrl.For(text);
            var query = HttpUtility.ParseQueryString(new Uri(url).Query);

            Assert.Equal(text, query["text"]);
        }

        [Fact]
        public void Url_HasNoRawSpacesOrNewlines()
        {
            var url = ComposeUrl.For("記事の表題\nhttps://example.com/a b");
            Assert.DoesNotContain(" ", url);
            Assert.DoesNotContain("\n", url);
        }

        [Fact]
        public void Url_PointsAtCompose()
            => Assert.StartsWith("https://x.com/compose/post?", ComposeUrl.For("x"));
    }
}
