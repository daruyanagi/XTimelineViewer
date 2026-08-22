using System.Collections.Generic;

namespace XTimelineViewer.Models
{
    /// <summary>
    /// 拡張機能ひとつ分の有効・無効の状態（#398）。
    ///
    /// 拡張機能は <c>CoreWebView2Profile</c> 単位で有効・無効を切り替えられるので、
    /// 「どのプロファイルで有効か」をプロファイル ID ごとに持つ。
    ///
    /// 鍵にするのは<b>フォルダー名</b>。拡張機能 ID は
    /// <c>AddBrowserExtensionAsync</c> を呼ぶまで分からず、読み込み前の判断に使えない。
    /// </summary>
    public class ExtensionState
    {
        /// <summary>
        /// 新しく追加されたプロファイルでの既定。
        ///
        /// 既定を有効にしてあるので、初期状態は「置き場にあるものはどのプロファイルでも
        /// 有効」という単純な理解のまま使える。特定の拡張機能だけ新規プロファイルで
        /// 切っておきたい人だけがここを変える。
        /// </summary>
        public bool EnabledByDefault { get; set; } = true;

        /// <summary>
        /// プロファイル ID → 有効かどうか。<b>ここに無いプロファイルは
        /// <see cref="EnabledByDefault"/> に従う</b>（明示的に切り替えたものだけ記録する）。
        /// </summary>
        public Dictionary<string, bool> PerProfile { get; set; } = [];
    }
}
