using System;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// 投稿画面の URL。
    ///
    /// 同じ URL を <c>MainWindow.Post</c> の 4 か所で手書きしていた
    /// （外部ブラウザーで開く・下書きリセット・初期ナビゲート・メニュー）。
    /// X 側の事情で変える日が来たときに直し漏れる形なのでまとめる。
    /// <see cref="AppUrls"/> を作ったのと同じ理由（#382）。
    ///
    /// <c>?text=</c> は本文の初期値。いまは呼び出し元がいないが、
    /// 共有やリンクの引用から流す口として用意してある（#431）。
    /// <b>載せるものは必ずエスケープする。</b> 記事の URL は
    /// <c>?</c> <c>&amp;</c> <c>#</c> を含むのが普通で、素で繋ぐとそこから先が
    /// 落ち、利用者には「本文が途中で切れている」形に見える。
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
