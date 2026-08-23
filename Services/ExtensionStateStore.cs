using System;
using System.Collections.Generic;
using System.IO;
using XTimelineViewer.Models;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// 拡張機能の有効・無効の読み書き（#398）。
    ///
    /// 判断の規則をここ 1 か所に閉じ込める。UI からもプロファイル読み込みからも
    /// 同じ答えが返らないと、「設定では ON なのにペインでは効かない」が起きる。
    ///
    /// UI に依存させない（テストプロジェクトからリンクして検証するため）。
    /// </summary>
    internal static class ExtensionStateStore
    {
        /// <summary>
        /// 状態の鍵。フォルダー名を使う。拡張機能 ID は
        /// <c>AddBrowserExtensionAsync</c> を呼ぶまで分からないため、
        /// 「読み込む前に有効かどうかを知りたい」場面で使えない。
        /// </summary>
        internal static string KeyOf(string extensionDir)
            => Path.GetFileName(extensionDir.TrimEnd(Path.DirectorySeparatorChar));

        /// <summary>
        /// このプロファイルで有効か。
        ///
        /// 明示的に切り替えられていなければ<b>その拡張機能の既定</b>に従う。
        /// 記録が無い拡張機能そのものも既定（有効）として扱う。
        /// </summary>
        internal static bool IsEnabled(
            IReadOnlyDictionary<string, ExtensionState> states, string key, string profileId)
        {
            if (!states.TryGetValue(key, out var state)) return true;
            if (state.PerProfile.TryGetValue(profileId, out var enabled)) return enabled;
            return state.EnabledByDefault;
        }

        /// <summary>このプロファイルでの有効・無効を記録する。</summary>
        internal static void SetEnabled(
            Dictionary<string, ExtensionState> states, string key, string profileId, bool enabled)
        {
            if (!states.TryGetValue(key, out var state))
            {
                state = new ExtensionState();
                states[key] = state;
            }
            state.PerProfile[profileId] = enabled;
        }

        /// <summary>新しく追加されたプロファイルでの既定を記録する。</summary>
        internal static void SetEnabledByDefault(
            Dictionary<string, ExtensionState> states, string key, bool enabled)
        {
            if (!states.TryGetValue(key, out var state))
            {
                state = new ExtensionState();
                states[key] = state;
            }
            state.EnabledByDefault = enabled;
        }

        /// <summary>入手先を記録する（#404）。</summary>
        internal static void SetSource(
            Dictionary<string, ExtensionState> states, string key, string? repoUrl, string? assetUrl)
        {
            if (!states.TryGetValue(key, out var state))
            {
                state = new ExtensionState();
                states[key] = state;
            }
            state.SourceRepoUrl  = repoUrl;
            state.SourceAssetUrl = assetUrl;
        }

        /// <summary>
        /// 一覧に出す入手先（#404）。記録があればそれを、無ければ
        /// <c>manifest.json</c> の <c>homepage_url</c> を使う。
        /// 手で置いたものは記録が無いので、後者だけが手がかりになる。
        /// </summary>
        internal static string? SourceUrlFor(
            IReadOnlyDictionary<string, ExtensionState> states, string key, string? homepageUrl)
        {
            if (states.TryGetValue(key, out var state) && !string.IsNullOrWhiteSpace(state.SourceRepoUrl))
                return state.SourceRepoUrl;

            return string.IsNullOrWhiteSpace(homepageUrl) ? null : homepageUrl;
        }

        /// <summary>アンインストールしたときに記録を捨てる。</summary>
        internal static void Forget(Dictionary<string, ExtensionState> states, string key)
            => states.Remove(key);

        /// <summary>
        /// 無くなった拡張機能・プロファイルの記録を落とす。
        ///
        /// 放っておくと settings.json に消えたものの記録が溜まり続け、
        /// 同じ名前で入れ直したときに<b>昔の設定が蘇る</b>。
        /// </summary>
        internal static int Prune(
            Dictionary<string, ExtensionState> states,
            IEnumerable<string> liveKeys,
            IEnumerable<string> liveProfileIds)
        {
            var keys     = new HashSet<string>(liveKeys, StringComparer.OrdinalIgnoreCase);
            var profiles = new HashSet<string>(liveProfileIds, StringComparer.Ordinal);

            var removed = 0;

            foreach (var key in new List<string>(states.Keys))
            {
                if (!keys.Contains(key))
                {
                    states.Remove(key);
                    removed++;
                    continue;
                }

                var perProfile = states[key].PerProfile;
                foreach (var pid in new List<string>(perProfile.Keys))
                {
                    if (profiles.Contains(pid)) continue;
                    perProfile.Remove(pid);
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>
        /// 拡張機能のフォルダーを消す。プロファイル側の登録解除は呼び出し側の責任。
        /// <b>登録を外す前に消すと、登録だけ残って壊れた状態になる</b>。
        /// </summary>
        internal static bool DeleteFolder(string extensionDir)
        {
            try
            {
                if (Directory.Exists(extensionDir)) Directory.Delete(extensionDir, recursive: true);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error($"ExtensionStateStore.DeleteFolder({extensionDir})", ex);
                return false;
            }
        }
    }
}
