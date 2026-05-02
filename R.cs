using Microsoft.Windows.ApplicationModel.Resources;

namespace XTimelineViewer
{
    internal static class R
    {
        private static readonly ResourceLoader _loader = new();
        public static string Get(string key) => _loader.GetString(key);
    }
}
