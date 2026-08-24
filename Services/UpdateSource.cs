namespace XTimelineViewer.Services
{
    /// <summary>
    /// 更新の有無をどこに聞くか（#412）。
    ///
    /// <b>winget 版は winget の答えだけを信じる。</b>
    /// 以前は winget の照会に失敗すると GitHub Releases の最新を見せていたが、
    /// winget 版の利用者にとって意味があるのは「winget で入手できる版」だけ。
    /// GitHub には出ているが winget-pkgs の PR が止まっている、という状態は
    /// 実際に起きており（v2.0.3 / v2.0.4）、そこで「更新があります」と言うと
    /// ［終了して更新］を押しても winget は何もせずに終わる空振りになる。
    /// 聞けなかったときは「確認できなかった」に倒す。
    /// </summary>
    internal static class UpdateSource
    {
        internal enum Kind
        {
            /// <summary>winget に聞く。</summary>
            Winget,
            /// <summary>GitHub Releases に聞く。</summary>
            GitHub,
            /// <summary>聞ける相手がいない。「確認できなかった」として扱う。</summary>
            Unavailable,
        }

        /// <param name="wingetPresent">winget.exe が見つかったか。</param>
        internal static Kind For(InstallChannel channel, bool wingetPresent) => channel switch
        {
            // MSIX 版は Store / Windows Update に任せる。ここまで来ない想定だが、
            // 来たときに GitHub の版を見せても入れ替える手段が無い。
            InstallChannel.Packaged => Kind.Unavailable,

            // winget 版で winget が無いのは異常事態。GitHub に聞いて
            // 「更新があります」と言っても、更新の実行も winget 頼みなので届かない。
            InstallChannel.Winget   => wingetPresent ? Kind.Winget : Kind.Unavailable,

            // ZIP 版は winget を持たないことがあるので GitHub に聞く（#328）。
            _                       => Kind.GitHub,
        };
    }
}
