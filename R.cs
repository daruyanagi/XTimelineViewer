using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace XTimelineViewer
{
    internal static class R
    {
        private static Dictionary<string, string> _dict = [];
        private static bool _initialized;

        internal static void Initialize(string? languageOverride = null)
        {
            if (_initialized) return;
            _initialized = true;

            // ResourceMap.GetValue() はドットを含むキー（x:Uid バインディング用）を
            // 特定の言語コンテキストで正しく解決できず COMException をスローする (#40)。
            // すべての言語で resw 直接パースに統一して回避する。
            var locale = languageOverride ?? ResolveSystemLocale();
            _dict = LoadResw(locale) ?? LoadResw("en-US") ?? [];
        }

        // 実行中に言語を切り替えるためリソース辞書を再読み込みする (#117)。
        // languageOverride が null の場合はシステム言語にフォールバックする。
        internal static void Reload(string? languageOverride = null)
        {
            _initialized = false;
            Initialize(languageOverride);
        }

        // システム言語から使用する resw ロケールフォルダー名を決定する。
        // ja 系は "ja-JP"、それ以外は "en-US" にフォールバックする。
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

        private static Dictionary<string, string>? LoadResw(string locale)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Strings", locale, "Resources.resw");
            if (!File.Exists(path)) return null;

            var dict = new Dictionary<string, string>();
            try
            {
                foreach (var data in XDocument.Load(path).Descendants("data"))
                {
                    var name  = data.Attribute("name")?.Value;
                    var value = data.Element("value")?.Value;
                    if (name != null && value != null)
                        dict[name] = value;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[R] LoadResw({locale}) FAILED: {ex.Message}");
                return null;
            }
            return dict;
        }

        public static string Get(string key)
        {
            if (!_initialized) Initialize();
            return _dict.TryGetValue(key, out var val) ? val : string.Empty;
        }
    }
}
