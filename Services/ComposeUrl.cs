using System;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// 投稿画面の URL（#431）。
    ///
    /// X は <c>?text=</c> を本文の初期値として扱う。Windows Share から
    /// 受け取った文言やリンクをここへ載せる。
    ///
    /// <b>載せるものは必ずエスケープする。</b> 共有されるのは記事の URL が
    /// ほとんどで、<c>?</c> <c>&amp;</c> <c>#</c> を含むのが普通。素で繋ぐと
    /// そこから先が落ちて、利用者には「本文が途中で切れている」形に見える。
    /// </summary>
    internal static class ComposeUrl
    {
        internal const string Base = "https://x.com/compose/post";

        /// <summary>初期テキスト付きの URL。テキストが無ければ素の URL。</summary>
        internal static string For(string? initialText)
            => string.IsNullOrWhiteSpace(initialText)
                ? Base
                : $"{Base}?text={Uri.EscapeDataString(initialText)}";
    }
}
