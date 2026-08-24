using XTimelineViewer;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services
{
    /// <summary>
    /// 更新の有無をどこに聞くか（#412）。
    ///
    /// <b>ここが緩むと「更新があります」の空振りが戻る。</b>
    /// winget 版に GitHub の最新を見せると、［終了して更新］を押しても
    /// winget にその版がまだ無いので何も起きない。
    /// </summary>
    public class UpdateSourceTests
    {
        [Fact]
        public void Winget_AsksWinget()
            => Assert.Equal(UpdateSource.Kind.Winget,
                UpdateSource.For(InstallChannel.Winget, wingetPresent: true));

        [Fact]
        public void Winget_WithoutWinget_DoesNotFallBackToGitHub()
        {
            // ここが GitHub になると、winget にまだ無い版で「更新があります」と
            // 言ってしまう。更新の実行も winget 頼みなので、押しても届かない。
            Assert.Equal(UpdateSource.Kind.Unavailable,
                UpdateSource.For(InstallChannel.Winget, wingetPresent: false));
        }

        [Fact]
        public void Zip_AsksGitHub()
        {
            // ZIP 版は winget を持たないことがある（#328）。
            // winget の有無に関わらず GitHub に聞く。
            Assert.Equal(UpdateSource.Kind.GitHub,
                UpdateSource.For(InstallChannel.Zip, wingetPresent: false));
            Assert.Equal(UpdateSource.Kind.GitHub,
                UpdateSource.For(InstallChannel.Zip, wingetPresent: true));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Packaged_AsksNobody(bool wingetPresent)
        {
            // MSIX 版は Store / Windows Update に任せる。
            // 版を見せても、ここから入れ替える手段が無い。
            Assert.Equal(UpdateSource.Kind.Unavailable,
                UpdateSource.For(InstallChannel.Packaged, wingetPresent));
        }
    }
}
