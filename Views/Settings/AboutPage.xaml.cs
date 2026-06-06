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
            var versionInfoText = $"XTimelineViewer v{versionStr}\r\n{edgeChannel} {edgeVersion}";

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
        }

        private void BuildHeaderCard(string versionStr, string versionInfoText)
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "StoreLogo.png");

            var textStack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text       = "XTimelineViewer",
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

            string? latestReleaseUrl = null;
            var settings = _parent.Settings;

            var statusText = new TextBlock
            {
                FontSize   = 13,
                Margin     = new Thickness(0, 4, 0, 0),
                Visibility = Visibility.Collapsed,
            };

            bool hasWinget = _parent.HasWinget;
            var updateBtnLabel = PackageContext.IsPackaged
                ? R.Get("CheckUpdate_Download_Store")
                : hasWinget
                    ? R.Get("CheckUpdate_Download_Winget")
                    : R.Get("CheckUpdate_Download_GitHub");
            var updateBtn = new Button
            {
                Content    = updateBtnLabel,
                Margin     = new Thickness(0, 4, 0, 0),
                Visibility = Visibility.Collapsed,
            };

            // キャッシュがあれば初期表示
            if (settings.CachedLatestVersion is { } cached
                && Version.TryParse(cached.TrimStart('v'), out var cachedVersion)
                && (_parent.CheckIsUpdateAvailable?.Invoke(currentVersion, cachedVersion) ?? false))
            {
                latestReleaseUrl      = $"{repoUrl}/releases/tag/{cached}";
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
                    if (_parent.FetchLatestReleaseAsync is null) return;
                    var (latest, tag, freshUrl) = await _parent.FetchLatestReleaseAsync();
                    latestReleaseUrl = freshUrl;
                    settings.LastUpdateCheck = DateTime.UtcNow.ToString("O");
                    if (_parent.CheckIsUpdateAvailable?.Invoke(currentVersion, latest) ?? false)
                    {
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
                var url = latestReleaseUrl ?? fallbackUrl;
                if (PackageContext.IsPackaged)
                {
                    if (_parent.LaunchUriAsync is not null)
                        await _parent.LaunchUriAsync(new Uri("ms-windows-store://pdp/?ProductId=9P308HB5BLJ1"));
                }
                else if (hasWinget)
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
                }
                else
                {
                    if (_parent.LaunchUriAsync is not null)
                        await _parent.LaunchUriAsync(new Uri(url));
                }
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

            // TwitterTimelineLoader
            var ttlLinkBtn = new HyperlinkButton
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
                            Text              = "Chrome Web Store",
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                        new FontIcon
                        {
                            Glyph             = "",
                            FontFamily        = new FontFamily("Segoe Fluent Icons"),
                            FontSize          = 10,
                            Opacity           = 0.6,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    }
                },
            };
            ttlLinkBtn.Click += async (_, _) =>
            {
                var uri = new Uri("https://chromewebstore.google.com/detail/twittertimelineloader/ipmgjpmedafkmmadinmeoannpofakpbh");
                if (_parent?.LaunchUriAsync is not null)
                    await _parent.LaunchUriAsync(uri);
                else
                    await Windows.System.Launcher.LaunchUriAsync(uri);
            };

            var ttlCard = new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header  = "TwitterTimelineLoader",
                Content = ttlLinkBtn,
            };
            expander.Items.Add(ttlCard);

            RootPanel.Children.Add(expander);
        }
    }
}
