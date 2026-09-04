using System;
using System.Collections.Generic;
using System.Web;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// フィードバックの URL（#426）。
    ///
    /// <b>本文が query に載る以上、素で繋ぐと壊れる。</b>
    /// 環境の値には空白・改行が必ず入るし（OS 版）、
    /// <c>&amp;</c> や <c>#</c> が紛れれば、そこから先が本文として届かない。
    /// 利用者は「書いたはずの症状が消えている」形で踏むので、気づきにくい。
    /// </summary>
    public class FeedbackUrlTests
    {
        private static readonly (string Label, string Value)[] Env =
        [
            ("アプリバージョン", "v2.1.0 (zip)"),
            ("WebView2",         "152.0.4191.53"),
            ("OS",               "Microsoft Windows NT 10.0.26200.0"),
        ];

        [Fact]
        public void Body_ListsTheEnvironment_AndEndsWithTheSymptomsHeading()
        {
            var body = FeedbackUrl.BuildBody(Env, "具体的な症状");

            Assert.Contains("- アプリバージョン: v2.1.0 (zip)\n", body);
            Assert.Contains("- WebView2: 152.0.4191.53\n", body);
            // 最後は書いてもらう場所。ここが無いと、環境だけ送られてくる。
            Assert.EndsWith("- 具体的な症状:\n", body);
        }

        [Fact]
        public void Body_WithNoEnvironment_StillAsksForSymptoms()
        {
            var body = FeedbackUrl.BuildBody([], "Symptoms");
            Assert.Equal("- Symptoms:\n", body);
        }

        [Fact]
        public void Url_CarriesTheBodyIntact()
        {
            // 一番効く確認。組み立てた URL から本文を取り出して、元と一致すること。
            var body = FeedbackUrl.BuildBody(Env, "具体的な症状");
            var url  = FeedbackUrl.For("https://github.com/o/r/issues/new", body);

            var query = HttpUtility.ParseQueryString(new Uri(url).Query);
            Assert.Equal(body, query["body"]);
        }

        [Theory]
        [InlineData("A & B")]              // & は query の区切り
        [InlineData("build #123")]         // # から先は fragment になる
        [InlineData("key=value")]
        [InlineData("100% done")]
        [InlineData("日本語 テキスト")]
        public void Url_SurvivesValuesThatWouldBreakTheQuery(string value)
        {
            var body = FeedbackUrl.BuildBody([("環境", value)], "症状");
            var url  = FeedbackUrl.For("https://github.com/o/r/issues/new", body);

            var query = HttpUtility.ParseQueryString(new Uri(url).Query);
            Assert.Equal(body, query["body"]);
            Assert.Contains(value, query["body"]);
        }

        [Fact]
        public void Url_AsksForTheBugLabel()
        {
            var url = FeedbackUrl.For("https://github.com/o/r/issues/new", "x");
            Assert.Contains("labels=bug", url);
        }

        [Fact]
        public void Url_HasNoRawNewlines()
        {
            // 改行が生で残ると、クリップボード経由やログで行が割れる。
            var url = FeedbackUrl.For("https://e/i", FeedbackUrl.BuildBody(Env, "症状"));
            Assert.DoesNotContain("\n", url);
            Assert.DoesNotContain(" ", url);
        }
    }
}
