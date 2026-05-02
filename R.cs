using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace XTimelineViewer
{
    internal static class R
    {
        private static ResourceMap? _map;
        private static Dictionary<string, string>? _overrideDict;
        private static bool _initialized;

        internal static void Initialize(string? languageOverride = null)
        {
            if (_initialized) return;
            _initialized = true;
            Debug.WriteLine("[R] Initialize() start");

            try
            {
                _map = new ResourceManager().MainResourceMap.GetSubtree("Resources");
                Debug.WriteLine("[R] ResourceMap acquired");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[R] ResourceMap FAILED: {ex.GetType().Name}: {ex.Message}");
            }

            if (languageOverride != null)
            {
                var reswPath = Path.Combine(
                    AppContext.BaseDirectory, "Strings", languageOverride, "Resources.resw");
                Debug.WriteLine($"[R] Looking for resw: {reswPath}");
                if (File.Exists(reswPath))
                {
                    _overrideDict = ParseResw(reswPath);
                    Debug.WriteLine($"[R] Loaded {_overrideDict.Count} strings from {languageOverride}");
                }
                else
                {
                    Debug.WriteLine($"[R] resw not found: {reswPath}");
                }
            }
        }

        private static Dictionary<string, string> ParseResw(string path)
        {
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
                Debug.WriteLine($"[R] ParseResw FAILED: {ex.Message}");
            }
            return dict;
        }

        public static string Get(string key)
        {
            if (!_initialized) Initialize();

            if (_overrideDict != null && _overrideDict.TryGetValue(key, out var overrideVal))
                return overrideVal;

            if (_map is null) return string.Empty;
            try { return _map.GetValue(key)?.ValueAsString ?? string.Empty; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[R] Get({key}) FAILED: {ex.GetType().Name}: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
