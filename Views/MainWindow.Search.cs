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
                : saved.Where(q => q.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
            sender.ItemsSource = filtered.Count > 0 ? filtered : null;
        }

        private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is string chosen)
                sender.Text = chosen;
        }

        private async Task OpenSearchDialogAsync(string query)
        {
            var profileId = ResolveComposeProfileId();

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

            string currentQuery = query;

            await InitSearchWebView(webView, profileId, q => currentQuery = q);
            webView.Source = new Uri(BuildSearchUrl(query));

            foreach (var wv in _webViews)
                wv.Visibility = Visibility.Collapsed;
            try
            {
                var result = await ShowDialogAsync(dlg);
                if (result == ContentDialogResult.Primary)
                {
                    var finalQuery = ExtractQueryFromUrl(webView.Source?.ToString()) ?? currentQuery;
                    if (!string.IsNullOrEmpty(finalQuery))
                    {
                        var url = BuildSearchUrl(finalQuery);
                        AddTimeline(CreateDefaultConfig(url));
                        AddSavedSearchQuery(finalQuery);
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

        private void AddSavedSearchQuery(string query)
        {
            var saved = _appSettings.SavedSearchQueries;
            saved.Remove(query);
            saved.Insert(0, query);
            SaveSettings();
        }

        private async Task InitSearchWebView(WebView2 webView, string profileId, Action<string> onQueryChanged)
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

                var q = ExtractQueryFromUrl(webView.Source?.ToString());
                if (q is not null) onQueryChanged(q);

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
    }
}
