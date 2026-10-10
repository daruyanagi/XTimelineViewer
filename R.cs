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

        // PrimaryLanguageOverride を設定すると CurrentUICulture も上書き後の言語を返すように
        // なるため、起動時（最初の型アクセス時 = override 設定前）に一度だけ取得して保持する。
        private static readonly string SystemLocale = ResolveSystemLocale();

        internal static void Initialize(string? languageOverride = null)
        {
            // x:Uid で解決される XAML リソース（#199 で導入予定）にも反映させるため、
            // プロセス全体の言語も上書きする。"" によるクリアは効かないことがあるため、
            // システム選択時は起動時に捕捉したシステム言語を明示的に設定する。
            try
            {
                Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride =
                    languageOverride ?? SystemLocale;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[R] PrimaryLanguageOverride FAILED: {ex.Message}");
            }

            (_manager, _map) = OpenResources();

            // 実行中の言語切り替え (#117) を確実にするため、明示的な ResourceContext で解決する。
            // システム選択時も明示的に修飾子を設定する。クリア（""）した PrimaryLanguageOverride が
            // 反映されず英語のまま残るケースがあるため、既定の修飾子に依存しない。
            _context = _manager.CreateResourceContext();
            _context.QualifierValues["Language"] = languageOverride ?? SystemLocale;
        }

        /// <summary>
        /// リソースを開く（#431）。
        ///
        /// <b>パッケージ ID を持つと、既定の <see cref="ResourceManager"/> は
        /// パッケージ側の PRI を見る。</b> このアプリの配布は unpackaged で、
        /// Share の受信（#431）のために空の MSIX で ID だけ付ける形を採るため、
        /// パッケージ側に文言は入っていない。そのまま呼ぶと
        /// <c>ResourceMap が見つかりません</c>（0x80073B1F）で起動に失敗する。
        ///
        /// まず既定を試し、見つからなければ exe の隣の <c>resources.pri</c> を開く。
        /// 本物の MSIX にした場合も、先に既定が当たるので壊れない。
        /// </summary>
        private static (ResourceManager Manager, ResourceMap Map) OpenResources()
        {
            try
            {
                var manager = new ResourceManager();
                return (manager, manager.MainResourceMap.GetSubtree("Resources"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[R] 既定の ResourceMap を開けなかった: {ex.Message}");
            }

            var local = System.IO.Path.Combine(AppContext.BaseDirectory, "resources.pri");
            var fallback = new ResourceManager(local);
            return (fallback, fallback.MainResourceMap.GetSubtree("Resources"));
        }

        // システム言語から使用するロケールを決定する。
        // ja 系は "ja-JP"、それ以外は "en-US" にフォールバックする（旧実装と同じ挙動）。
        private static string ResolveSystemLocale()
        {
            try
            {
                var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                return lang == "ja" ? "ja-JP" : "en-US";
            }
            catch
            {
                return "en-US";
            }
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
