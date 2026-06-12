using System;
using System.Diagnostics;
using Microsoft.Windows.ApplicationModel.Resources;

namespace XTimelineViewer
{
    /// <summary>
    /// 多言語リソースへのアクセスを提供する。
    /// MRT Core (ResourceManager) でビルド時生成の resources.pri から文字列を解決する (#198)。
    /// WinAppSDK 1.6 以降は Microsoft.Windows.Globalization.ApplicationLanguages により
    /// unpackaged でも PrimaryLanguageOverride が有効。ただし unpackaged では
    /// セッション間で永続化されないため、起動のたびに設定する。
    /// </summary>
    internal static class R
    {
        private static ResourceManager? _manager;
        private static ResourceMap?     _map;
        private static ResourceContext? _context;

        internal static void Initialize(string? languageOverride = null)
        {
            // x:Uid で解決される XAML リソース（#199 で導入予定）にも反映させるため、
            // 明示コンテキストとは別にプロセス全体の言語も上書きする。
            try
            {
                Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride =
                    languageOverride ?? "";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[R] PrimaryLanguageOverride FAILED: {ex.Message}");
            }

            _manager = new ResourceManager();
            _map     = _manager.MainResourceMap.GetSubtree("Resources");

            // 実行中の言語切り替え (#117) を確実にするため、明示的な ResourceContext で解決する。
            // 既定コンテキストはプロセス起動時の言語をキャッシュすることがある。
            _context = _manager.CreateResourceContext();
            if (languageOverride is not null)
                _context.QualifierValues["Language"] = languageOverride;
        }

        // 実行中に言語を切り替えるためリソースコンテキストを再構築する (#117)。
        // languageOverride が null の場合はシステム言語にフォールバックする。
        internal static void Reload(string? languageOverride = null)
            => Initialize(languageOverride);

        public static string Get(string key)
        {
            if (_map is null || _context is null) Initialize();

            try
            {
                // x:Uid 形式のキー（例: PostLabel.Text）は PRI 内では PostLabel/Text として
                // 格納されるため変換する。ドットのまま GetValue すると COMException になる (#40)。
                var candidate = _map!.TryGetValue(key.Replace('.', '/'), _context);
                return candidate?.ValueAsString ?? string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[R] Get({key}) FAILED: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
