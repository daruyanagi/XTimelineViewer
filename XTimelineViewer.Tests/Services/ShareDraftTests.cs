using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// 共有で受け取ったものを下書きに直す（#431）。
    ///
    /// <b>共有元によって入っているものが違う。</b> Edge は表題とリンクを両方、
    /// メモ帳の選択範囲はテキストだけ、リンクのコピーはリンクだけ。
    /// 素朴に繋ぐと、リンクが本文に二重で並ぶ。
    /// </summary>
    public class ShareDraftTests
    {
        private const string Link = "https://example.com/article";

        [Fact]
        public void TextOnly_IsUsedAsIs()
            => Assert.Equal("メモの選択範囲", ShareDraft.Build("メモの選択範囲", null));

        [Fact]
        public void LinkOnly_IsUsedAsIs()
            => Assert.Equal(Link, ShareDraft.Build(null, Link));

        [Fact]
        public void TextAndLink_ArePutOnSeparateLines()
        {
            // X の本文としてそのまま読める並びにする。
            Assert.Equal($"記事の表題\n{Link}", ShareDraft.Build("記事の表題", Link));
        }

        [Fact]
        public void SameTextAndLink_IsNotDoubled()
        {
            // リンクをコピーして共有すると、両方に同じものが入ることがある。
            Assert.Equal(Link, ShareDraft.Build(Link, Link));
        }

        [Fact]
        public void TextAlreadyContainingTheLink_IsLeftAlone()
        {
            // Edge は「表題 + 改行 + リンク」をテキストに入れてくることがある。
            // そこへリンクをもう一度足すと、本文に 2 回並ぶ。
            var text = $"記事の表題\n{Link}";
            Assert.Equal(text, ShareDraft.Build(text, Link));
        }

        [Fact]
        public void Nothing_GivesNull()
        {
            Assert.Null(ShareDraft.Build(null, null));
            Assert.Null(ShareDraft.Build("   ", null));
            Assert.Null(ShareDraft.Build("", "  "));
        }

        [Theory]
        [InlineData("  前後に空白  ", "前後に空白")]
        [InlineData("\n改行つき\n", "改行つき")]
        public void Whitespace_IsTrimmed(string input, string expected)
            => Assert.Equal(expected, ShareDraft.Build(input, null));

        [Fact]
        public void CaseDiffering_KeepsTheCanonicalLink()
        {
            // 共有元が大文字小文字を変えてくることがある。二重にしないうえで、
            // 採るのは共有元が持つリンクのほう。URL のパスは大文字小文字を
            // 区別するので、見た目を変えられたテキストより信用できる。
            Assert.Equal(Link, ShareDraft.Build("HTTPS://EXAMPLE.COM/ARTICLE", Link));
        }
    }
}
