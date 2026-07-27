using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;
using System.Reflection;
using Windows.ApplicationModel.DataTransfer;

namespace XTimelineViewer.Views.Settings
{
    public sealed partial class AboutPage : Page
    {
        private SettingsWindow? _parent;

        public AboutPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _parent = e.Parameter as SettingsWindow;
            PopulateUI();
        }

        private void PopulateUI()
        {
            PageTitle.Text = R.Get("Nav_About");

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version!;
            var versionStr = currentVersion.ToString(3);
            var edgeChannel = R.Get("EdgeChannel_Runtime");
            var edgeVersion = _parent?.EdgeVersion ?? R.Get("Version_Unknown");
            var versionInfoText = $"XTimelineViewer (xTV) v{versionStr}\r\n{edgeChannel} {edgeVersion}";

            var repoUrl = "https://github.com/daruyanagi/XTimelineViewer";
            var fallbackUrl = repoUrl + "/releases/latest";

            // ── 1. アプリ情報ヘッダー ────────────────────────────────────────
            BuildHeaderCard(versionStr, versionInfoText);

            // ── 2. 更新を確認 ────────────────────────────────────────────────
            BuildUpdateSection(currentVersion, repoUrl, fallbackUrl);

            // ── 3. ライセンス ────────────────────────────────────────────────
            var licenseCard = new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header     = R.Get("About_License"),
                HeaderIcon = new FontIcon
                {
                    Glyph      = "",
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                },
                Content = new TextBlock
                {
                    Text     = "MIT License",
                    FontSize = 13,
                    Opacity  = 0.8,
                    IsTextSelectionEnabled = true,
                },
            };
            RootPanel.Children.Add(licenseCard);

            // ── 4. 利用しているコンポーネント ────────────────────────────────
            BuildComponentsExpander(edgeChannel, edgeVersion);

            // ── 5. 謝辞 ──────────────────────────────────────────────────────
            BuildAcknowledgementsExpander();
        }

        private void BuildHeaderCard(string versionStr, string versionInfoText)
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "StoreLogo.png");

            var textStack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text       = "XTimelineViewer (xTV)",
                FontSize   = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            textStack.Children.Add(new TextBlock { Text = $"v{versionStr}", FontSize = 13, Opacity = 0.7 });
            textStack.Children.Add(new TextBlock { Text = R.Get("About_Copyright"), FontSize = 12, Opacity = 0.6 });

            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing     = 12,
            };
            if (File.Exists(iconPath))
                titleRow.Children.Add(new Image
                {
                    Source            = new BitmapImage(new Uri(iconPath)),
                    Width             = 48,
                    Height            = 48,
                    VerticalAlignment = VerticalAlignment.Top,
                });
            titleRow.Children.Add(textStack);

            var copyBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing     = 6,
                    Children    =
                    {
                        new FontIcon
                        {
                            Glyph      = "",
                            FontFamily = new FontFamily("Segoe Fluent Icons"),
                            FontSize   = 14,
                        },
                        new TextBlock { Text = R.Get("Button_Copy") },
                    }
                },
            };
            copyBtn.Click += (_, _) =>
            {
                var dp = new DataPackage();
                dp.SetText(versionInfoText);
                Clipboard.SetContent(dp);
            };

            var headerCard = new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header  = titleRow,
                Content = copyBtn,
            };
            RootPanel.Children.Add(headerCard);
        }

        private void BuildUpdateSection(Version currentVersion, string repoUrl, string fallbackUrl)
        {
            if (_parent is null) return;

            // MSIX 版は Store の自動更新に任せる / winget がなければ更新チェック不要
            bool hasWinget = _parent.HasWinget;
            if (PackageContext.IsPackaged || !hasWinget) return;

            var settings = _parent.Settings;

            var statusText = new TextBlock
            {
                FontSize   = 13,
                Margin     = new Thickness(0, 4, 0, 0),
                Visibility = Visibility.Collapsed,
            };

            var updateBtn = new Button
            {
                Content    = R.Get("CheckUpdate_Download_Winget"),
                Margin     = new Thickness(0, 4, 0, 0),
                Visibility = Visibility.Collapsed,
            };

            // キャッシュがあれば初期表示
            if (settings.CachedLatestVersion is { } cached
                && Version.TryParse(cached.TrimStart('v'), out var cachedVersion)
                && cachedVersion > currentVersion)
            {
                statusText.Text       = string.Format(R.Get("CheckUpdate_Available"), cached);
                statusText.Visibility = Visibility.Visible;
                updateBtn.Visibility  = Visibility.Visible;
            }

            var checkBtn = new Button { Content = R.Get("CheckUpdate_Btn") };
            checkBtn.Click += async (_, _) =>
            {
                checkBtn.IsEnabled    = false;
                statusText.Text       = R.Get("CheckUpdate_Checking");
                statusText.Visibility = Visibility.Visible;
                updateBtn.Visibility  = Visibility.Collapsed;
                try
                {
                    if (_parent.FetchWingetLatestVersionAsync is null) return;
                    var latest = await _parent.FetchWingetLatestVersionAsync();
                    if (latest is not null && latest > currentVersion)
                    {
                        var tag = $"v{latest.ToString(3)}";
                        settings.CachedLatestVersion = tag;
                        statusText.Text      = string.Format(R.Get("CheckUpdate_Available"), tag);
                        updateBtn.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        settings.CachedLatestVersion = null;
                        statusText.Text = R.Get("CheckUpdate_Latest");
                    }
                    _parent.SaveSettingsOnly?.Invoke();
                    _parent.UpdateMenuBadge?.Invoke();
                }
                catch
                {
                    statusText.Text = R.Get("CheckUpdate_Error");
                }
                finally
                {
                    checkBtn.IsEnabled = true;
                }
            };

            updateBtn.Click += async (_, _) =>
            {
                var confirmDlg = new ContentDialog
                {
                    Title             = R.Get("CheckUpdate_WingetTitle"),
                    Content           = new TextBlock
                    {
                        Text         = R.Get("CheckUpdate_WingetBody"),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    PrimaryButtonText = R.Get("CheckUpdate_WingetConfirm"),
                    CloseButtonText   = R.Get("Button_Cancel"),
                    XamlRoot          = XamlRoot,
                    RequestedTheme    = ((FrameworkElement)_parent.Content).ActualTheme,
                };
                if (await confirmDlg.ShowAsync() != ContentDialogResult.Primary) return;
                _parent.ExitAndRunWingetUpdate?.Invoke();
            };

            var updateCard = new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header     = R.Get("CheckUpdate_Btn"),
                HeaderIcon = new FontIcon
                {
                    Glyph      = "",
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                },
            };

            var updateContent = new StackPanel { Spacing = 4 };
            updateContent.Children.Add(checkBtn);
            updateContent.Children.Add(statusText);
            updateContent.Children.Add(updateBtn);
            updateCard.Content = updateContent;

            RootPanel.Children.Add(updateCard);
        }

        private void BuildComponentsExpander(string edgeChannel, string edgeVersion)
        {
            var expander = new CommunityToolkit.WinUI.Controls.SettingsExpander
            {
                Header     = R.Get("About_Components"),
                HeaderIcon = new FontIcon
                {
                    Glyph      = "",
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                },
            };

            // WebView2
            var webView2Card = new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header = edgeChannel,
                Content = new TextBlock
                {
                    Text     = edgeVersion,
                    FontSize = 13,
                    Opacity  = 0.8,
                    IsTextSelectionEnabled = true,
                },
            };
            expander.Items.Add(webView2Card);

            RootPanel.Children.Add(expander);
        }

        // 謝辞（#207）。同梱していた TwitterTimelineLoader を内製化したため、原作への謝辞を掲載する。
        // あわせてアプリアイコンの制作者をクレジットする（#281）。
        private void BuildAcknowledgementsExpander()
        {
            var expander = new CommunityToolkit.WinUI.Controls.SettingsExpander
            {
                Header     = R.Get("About_Acknowledgements"),
                HeaderIcon = new FontIcon
                {
                    Glyph      = "\uE734",
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                },
            };

            // TwitterTimelineLoader（ホーム自動更新の元になった Chromium 拡張機能）
            expander.Items.Add(BuildLinkCard(
                "TwitterTimelineLoader",
                R.Get("About_Ack_TTL"),
                "https://chromewebstore.google.com/detail/twittertimelineloader/ipmgjpmedafkmmadinmeoannpofakpbh"));

            // アプリアイコンの制作者（#281）
            expander.Items.Add(BuildLinkCard(
                "keikipc",
                R.Get("About_Ack_Icon"),
                "https://crowdworks.jp/public/employees/7101047"));

            RootPanel.Children.Add(expander);
        }

        // リンクボタン付きの SettingsCard を作る（謝辞の各項目用）
        private CommunityToolkit.WinUI.Controls.SettingsCard BuildLinkCard(string header, string description, string url)
        {
            var linkBtn = new HyperlinkButton
            {
                Padding = new Thickness(0),
                Content = new StackPanel
                {
                    Orientation       = Orientation.Horizontal,
                    Spacing           = 4,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children          =
                    {
                        new TextBlock
                        {
                            Text              = R.Get("About_OpenLink"),
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                        new FontIcon
                        {
                            Glyph             = "\uE8A7",
                            FontFamily        = new FontFamily("Segoe Fluent Icons"),
                            FontSize          = 10,
                            Opacity           = 0.6,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    }
                },
            };
            linkBtn.Click += async (_, _) =>
            {
                var uri = new Uri(url);
                if (_parent?.LaunchUriAsync is not null)
                    await _parent.LaunchUriAsync(uri);
                else
                    await Windows.System.Launcher.LaunchUriAsync(uri);
            };

            return new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header      = header,
                Description = description,
                Content     = linkBtn,
            };
        }
    }
}
