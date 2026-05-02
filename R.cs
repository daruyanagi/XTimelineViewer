using Microsoft.Windows.ApplicationModel.Resources;

namespace XTimelineViewer
{
    internal static class R
    {
        private static ResourceMap? _map;
        private static bool _initialized;

        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            try { _map = new ResourceManager().MainResourceMap.GetSubtree("Resources"); }
            catch { }
        }

        public static string Get(string key)
        {
            if (!_initialized) Initialize();
            if (_map is null) return string.Empty;
            try { return _map.GetValue(key)?.ValueAsString ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
