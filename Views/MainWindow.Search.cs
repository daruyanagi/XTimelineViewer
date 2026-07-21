using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using XTimelineViewer.Services;

namespace XTimelineViewer.Views
{
    public sealed partial class MainWindow : Window
    {
        private void FocusSearchBox_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            SearchBox.Focus(FocusState.Programmatic);
            args.Handled = true;
        }

        // Ctrl+1〜9（WebView2 非フォーカス時もカバー）。Number1..Number9 は連続なので差分で番号を得る。
        private void FocusTimelineByNumber_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            int n = sender.Key - Windows.System.VirtualKey.Number1 + 1;
            FocusTimelineByIndex(n);
            args.Handled = true;
        }

        // Ctrl+←/→（WebView2 非フォーカス時もカバー）#227
        private void FocusAdjacentTimeline_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            FocusAdjacentFromActive(sender.Key == Windows.System.VirtualKey.Right ? +1 : -1);
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
                : saved.Where(q => SearchQueryHelper.DecodeSearchPath(q).Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
            sender.ItemsSource = filtered.Count > 0 ? filtered : null;
        }

        private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is string chosen)
                sender.Text = SearchQueryHelper.ExtractQueryFromSearchPath(chosen) ?? chosen;
        }

        private async Task OpenSearchDialogAsync(string input, bool showAddButton = true)
        {
            var profileId = ResolveComposeProfileId();

            // サジェストからのクエリパス or 直接入力のキーワード
            var initialUrl = input.StartsWith("/search?", StringComparison.OrdinalIgnoreCase)
                ? "https://x.com" + input
                : SearchQueryHelper.BuildSearchUrl(input);

            var webView = new WebView2 { Width = 500, MinHeight = 520 };

            var dlg = new ContentDialog
            {
                Title              = R.Get("Search_DialogTitle"),
                Content            = webView,
                // 「直前のリポストを検索」など閲覧目的のときは［タイムラインとして追加］を出さない（#315）
                PrimaryButtonText  = showAddButton ? R.Get("Search_AddBtn") : null,
                CloseButtonText    = R.Get("Button_Close"),
                XamlRoot           = Content.XamlRoot,
                DefaultButton      = showAddButton ? ContentDialogButton.Primary : ContentDialogButton.Close,
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
                    if (currentUrl is not null && SearchQueryHelper.ExtractQueryFromUrl(currentUrl) is not null)
                    {
                        AddTimeline(CreateDefaultConfig(currentUrl));
                        var searchPath = SearchQueryHelper.ExtractSearchPath(currentUrl);
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
            SearchQueryHelper.AddSavedQuery(_appSettings.SavedSearchQueries, searchPath);
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

        /// <summary>クエリパスをデコードして表示用文字列を返す。x:Bind から呼ばれるため MainWindow に残している。</summary>
        public static string DecodeSearchPath(string searchPath)
            => SearchQueryHelper.DecodeSearchPath(searchPath);
    }
}
