using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace XTimelineViewer.Views
{
    public sealed partial class MainWindow : Window
    {
        private void FocusSearchBox_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            SearchBox.Focus(FocusState.Programmatic);
            args.Handled = true;
        }

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion is string chosen)
            {
                _ = OpenSearchDialogAsync(chosen);
                return;
            }
            var query = (args.QueryText ?? sender.Text)?.Trim();
            if (string.IsNullOrEmpty(query)) return;
            _ = OpenSearchDialogAsync(query);
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            var text = sender.Text?.Trim() ?? "";
            var saved = _appSettings.SavedSearchQueries;
            if (saved.Count == 0)
            {
                sender.ItemsSource = null;
                return;
            }
            var filtered = string.IsNullOrEmpty(text)
                ? saved.ToList()
                : saved.Where(q => DecodeSearchPath(q).Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
            sender.ItemsSource = filtered.Count > 0 ? filtered : null;
        }

        private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is string chosen)
                sender.Text = ExtractQueryFromSearchPath(chosen) ?? chosen;
        }

        private async Task OpenSearchDialogAsync(string input)
        {
            var profileId = ResolveComposeProfileId();

            // サジェストからのクエリパス or 直接入力のキーワード
            var initialUrl = input.StartsWith("/search?", StringComparison.OrdinalIgnoreCase)
                ? "https://x.com" + input
                : BuildSearchUrl(input);

            var webView = new WebView2 { Width = 500, MinHeight = 520 };

            var dlg = new ContentDialog
            {
                Title              = R.Get("Search_DialogTitle"),
                Content            = webView,
                PrimaryButtonText  = R.Get("Search_AddBtn"),
                CloseButtonText    = R.Get("Button_Close"),
                XamlRoot           = Content.XamlRoot,
                DefaultButton      = ContentDialogButton.Primary,
            };

            await InitSearchWebView(webView, profileId);
            webView.Source = new Uri(initialUrl);

            foreach (var wv in _webViews)
                wv.Visibility = Visibility.Collapsed;
            try
            {
                var result = await ShowDialogAsync(dlg);
                if (result == ContentDialogResult.Primary)
                {
                    var currentUrl = webView.Source?.ToString();
                    if (currentUrl is not null && ExtractQueryFromUrl(currentUrl) is not null)
                    {
                        AddTimeline(CreateDefaultConfig(currentUrl));
                        var searchPath = ExtractSearchPath(currentUrl);
                        if (searchPath is not null)
                            AddSavedSearchQuery(searchPath);
                    }
                }
            }
            finally
            {
                foreach (var wv in _webViews)
                    wv.Visibility = Visibility.Visible;

                try { webView.Close(); } catch { }
            }

            SearchBox.Text = "";
        }

        private void AddSavedSearchQuery(string searchPath)
        {
            var saved = _appSettings.SavedSearchQueries;
            saved.Remove(searchPath);
            saved.Insert(0, searchPath);
            SaveSettings();
        }

        private async Task InitSearchWebView(WebView2 webView, string profileId)
        {
            var env = await GetOrCreateProfileEnvAsync(profileId);
            await webView.EnsureCoreWebView2Async(env);

            var root = (FrameworkElement)Content;
            var scheme = root.ActualTheme switch
            {
                ElementTheme.Light => CoreWebView2PreferredColorScheme.Light,
                ElementTheme.Dark  => CoreWebView2PreferredColorScheme.Dark,
                _                  => CoreWebView2PreferredColorScheme.Auto,
            };
            webView.CoreWebView2.Profile.PreferredColorScheme = scheme;

            webView.CoreWebView2.NavigationCompleted += async (s, args) =>
            {
                if (!args.IsSuccess) return;
                await webView.CoreWebView2.ExecuteScriptAsync("""
                    (function() {
                        var id = 'xtv-search-style';
                        if (document.getElementById(id)) return;
                        var s = document.createElement('style');
                        s.id = id;
                        s.textContent =
                            '[data-testid="sidebarColumn"],' +
                            'header[role="banner"]' +
                            '{display:none!important}' +
                            '[data-testid="primaryColumn"]{max-width:100%!important}';
                        document.head.appendChild(s);
                    })();
                    """);
            };
        }

        private static string BuildSearchUrl(string query)
        {
            var encoded = Uri.EscapeDataString(query);
            return $"https://x.com/search?q={encoded}&src=typed_query";
        }

        /// <summary>URL からクエリパス部分を抽出する（例: /search?q=test&amp;f=live）。src= は除外。</summary>
        private static string? ExtractSearchPath(string? url)
        {
            if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            if (!uri.AbsolutePath.Equals("/search", StringComparison.OrdinalIgnoreCase)) return null;
            var qs = uri.Query;
            if (string.IsNullOrEmpty(qs)) return null;
            var meaningful = qs.TrimStart('?').Split('&')
                .Where(p => !p.StartsWith("src=", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return meaningful.Length > 0
                ? "/search?" + string.Join("&", meaningful)
                : null;
        }

        /// <summary>URL から q= パラメータの値を抽出する。</summary>
        private static string? ExtractQueryFromUrl(string? url)
        {
            if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            var qs = uri.Query;
            if (string.IsNullOrEmpty(qs)) return null;
            foreach (var pair in qs.TrimStart('?').Split('&'))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && parts[0] == "q")
                    return Uri.UnescapeDataString(parts[1]);
            }
            return null;
        }

        /// <summary>クエリパスをデコードして表示用文字列を返す。x:Bind から呼ばれる。</summary>
        public static string DecodeSearchPath(string searchPath)
            => Uri.UnescapeDataString(searchPath);

        /// <summary>クエリパス（/search?q=...&amp;f=live）から q= の値を抽出する。</summary>
        private static string? ExtractQueryFromSearchPath(string searchPath)
        {
            return ExtractQueryFromUrl("https://x.com" + searchPath);
        }
    }
}
