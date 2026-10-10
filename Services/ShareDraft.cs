using System;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// 共有で受け取った文言とリンクを、投稿の下書きに組み立てる（#431）。
    ///
    /// 共有元によって入っているものが違う。Edge はリンクと表題を両方入れ、
    /// メモ帳の選択範囲はテキストだけ。<b>並べ方を 1 か所に決めておく。</b>
    ///
    /// <see cref="SharePayload"/> から切り出してあるのは、あちらが
    /// <c>Windows.ApplicationModel.DataTransfer</c> に依存していて
    /// テストプロジェクト（net8.0）からリンクできないため。
    /// </summary>
    internal static class ShareDraft
    {
        /// <summary>
        /// 下書きの本文。何も無ければ null。
        ///
        /// <b>文言のあとにリンクを置く。</b> X の本文としてそのまま読める並びにしたい。
        /// 文言がリンクそのもの、あるいはリンクを含んでいるなら重ねない
        /// （Edge は表題とリンクを両方入れてくるので、素朴に繋ぐと二重になる）。
        /// </summary>
        internal static string? Build(string? text, string? link)
        {
            text = Trim(text);
            link = Trim(link);

            if (link is null) return text;
            if (text is null) return link;

            if (string.Equals(text, link, StringComparison.OrdinalIgnoreCase)) return link;
            if (text.Contains(link, StringComparison.OrdinalIgnoreCase)) return text;

            return $"{text}\n{link}";
        }

        private static string? Trim(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
