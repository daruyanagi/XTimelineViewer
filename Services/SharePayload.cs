using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// Windows の共有シートから受け取った中身を、投稿の下書きに直す（#431）。
    ///
    /// 共有元によって入っているものが違う。Edge はリンクと表題を両方入れるし、
    /// メモ帳の選択範囲はテキストだけ。<b>組み立て方を 1 か所に決めておく。</b>
    ///
    /// UI 非依存。<see cref="ShareOperation"/> に触らない形（文字列を受けて
    /// 文字列を返す）にしてあるので、単体でテストできる。
    /// </summary>
    internal static class SharePayload
    {
        /// <summary>
        /// 共有の中身を読み出して下書きにする。読めなければ null。
        ///
        /// <b><see cref="ShareOperation.ReportCompleted"/> は呼び出し側で。</b>
        /// 下書きを UI へ渡し終える前に終了を報告すると、共有元から見て
        /// 「終わった」ことになってしまう。
        /// </summary>
        internal static async Task<string?> ReadDraftAsync(DataPackageView data)
        {
            string? text = null;
            string? link = null;

            try
            {
                if (data.Contains(StandardDataFormats.Text))
                    text = await data.GetTextAsync();
            }
            catch (Exception ex) { AppLog.Debug($"Share: テキストを読めなかった {ex.Message}"); }

            try
            {
                if (data.Contains(StandardDataFormats.WebLink))
                    link = (await data.GetWebLinkAsync())?.ToString();
            }
            catch (Exception ex) { AppLog.Debug($"Share: リンクを読めなかった {ex.Message}"); }

            return ShareDraft.Build(text, link);
        }
    }
}
