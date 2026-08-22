using System;
using System.Threading.Tasks;

namespace XTimelineViewer.Services
{
    internal static class TaskExtensions
    {
        /// <summary>
        /// 意図的に待たない非同期処理の失敗を記録する（#374）。
        ///
        /// UI イベントから非同期処理を蹴る場面が多く、待たないこと自体は妥当。
        /// 問題は <c>_ = SomethingAsync()</c> と書くと、
        /// <b>投げられた例外を誰も観測しない</b>こと。#339 はまさにこれで、
        /// InitWebViewAsync の後半 90 行が try の外にあり、失敗が完全に無言だった。
        ///
        /// 呼び出し先が自分で握って記録しているものは、そもそもここまで来ない。
        /// 追加しても二重記録にはならない。
        /// </summary>
        /// <param name="context">ログに出す手がかり。呼び出し先の名前を入れる。</param>
        internal static void FireAndForget(this Task task, string context)
            => _ = AwaitAndLogAsync(task, context);

        private static async Task AwaitAndLogAsync(Task task, string context)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                AppLog.Error($"FireAndForget({context})", ex);
            }
        }
    }
}
