using System;
using System.Collections.Generic;
using System.Linq;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// 「フィードバックを送る」の URL を組み立てる（#426）。
    ///
    /// 環境を書いてもらうのは負担が大きいし、たいてい抜ける。
    /// 起動時にログの見出しへ出しているのと同じもの（#340）を、
    /// あらかじめ本文に入れておく。
    ///
    /// 以前は旧 About ダイアログに同じ仕組みがあったが、
    /// #138 で設定ページへ移したときに引き継がれず消えていた。
    ///
    /// UI 非依存。WebView2 や WinAppSDK の版を調べるのは呼び出し側の仕事
    /// （テストプロジェクトは net8.0 でそれらの型に触れない）。
    /// </summary>
    internal static class FeedbackUrl
    {
        /// <summary>環境の行と、症状を書いてもらう見出しからなる本文。</summary>
        internal static string BuildBody(
            IEnumerable<(string Label, string Value)> environment, string symptomsLabel)
            => string.Concat(environment.Select(e => $"- {e.Label}: {e.Value}\n"))
             + $"- {symptomsLabel}:\n";

        /// <summary>
        /// 新規 issue の URL。
        ///
        /// <b>本文はパーセントエンコードして query に載せる。</b>
        /// OS 版の空白や、環境によっては値に紛れる <c>&amp;</c> <c>#</c> で
        /// そのままだと query が壊れ、本文が途中で切れる。
        /// </summary>
        internal static string For(string newIssueUrl, string body)
            => $"{newIssueUrl}?labels=bug&body={Uri.EscapeDataString(body)}";
    }
}
