using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;
using System.Reflection;
using Windows.ApplicationModel.DataTransfer;
using XTimelineViewer.Services;

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
            // 配布経路を併記して winget 版 / ZIP 版を見分けられるようにする（#327）。
            // サポート時に「winget upgrade で更新してください」と案内できるかの判断材料になる。
            var versionWithChannel = $"v{versionStr}（{ChannelLabel()}）";
            var edgeChannel = R.Get("EdgeChannel_Runtime");
            var edgeVersion = _parent?.EdgeVersion ?? R.Get("Version_Unknown");
            var versionInfoText = $"XTimelineViewer (xTV) {versionWithChannel}\r\n{edgeChannel} {edgeVersion}";


            // ── 1. アプリ情報ヘッダー ────────────────────────────────────────
            BuildHeaderCard(versionWithChannel, versionInfoText);

            // ── 2. 更新を確認 ────────────────────────────────────────────────
            BuildUpdateSection(currentVersion, AppUrls.LatestRelease);

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

        // 配布経路の表示名（#327）
        private static string ChannelLabel() => PackageContext.Channel switch
        {
            InstallChannel.Winget   => R.Get("About_Channel_Winget"),
            InstallChannel.Packaged => R.Get("About_Channel_Packaged"),
            _                       => R.Get("About_Channel_Zip"),
        };

        private void BuildHeaderCard(string versionText, string versionInfoText)
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "StoreLogo.png");

            var textStack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text       = "XTimelineViewer (xTV)",
                FontSize   = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            textStack.Children.Add(new TextBlock { Text = versionText, FontSize = 13, Opacity = 0.7 });
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

        /// <summary>
        /// 更新の確認と状態表示（#382）。
        ///
        /// 以前は SettingsCard の右側にボタン・状態文・ボタンを縦に積んでいた。
        /// 狭い上に、重要な「更新がある」が小さな地の文で埋もれていた。
        /// PowerToys にならい、操作（確認ボタン）と状態（InfoBar）を分ける。
        /// </summary>
        private void BuildUpdateSection(Version currentVersion, string releaseUrl)
        {
            if (_parent is null) return;

            // MSIX 版は Store / Windows Update の自動更新に任せる。
            // ZIP 版は winget を持たないことがあるが、GitHub Releases で確認できるので表示する (#328)。
            if (PackageContext.IsPackaged) return;

            // winget 版なら winget upgrade に委譲でき、それ以外はリリースページへ誘導する。
            // ZIP 版の「再起動して更新」は自己置き換えが要るので #328 段階2 待ち。
            bool useWinget = PackageContext.Channel == InstallChannel.Winget && _parent.HasWinget;

            var settings = _parent.Settings;

            var infoBar = new InfoBar
            {
                IsOpen     = true,
                IsClosable = false,
            };

            // 「最新の状態です」のときでも変更履歴を見に行けるようにしておく。
            var releaseLink = new HyperlinkButton { Content = R.Get("CheckUpdate_ReleasePage") };
            releaseLink.Click += (_, _) => OpenUri(new Uri(releaseUrl));

            var updateBtn = new Button
            {
                Content = useWinget ? R.Get("CheckUpdate_Download_Winget")
                                    : R.Get("CheckUpdate_Download_Zip"),
            };

            var checkBtn = new Button { Content = R.Get("CheckUpdate_Btn") };

            var updateCard = new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header     = R.Get("CheckUpdate_Btn"),
                HeaderIcon = new FontIcon
                {
                    Glyph      = "\uE895",
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                },
                Content = checkBtn,
            };

            void ShowChecking()
            {
                infoBar.Severity     = InfoBarSeverity.Informational;
                infoBar.Message      = R.Get("CheckUpdate_Checking");
                infoBar.ActionButton = null;
            }

            void ShowAvailable(string tag)
            {
                infoBar.Severity     = InfoBarSeverity.Warning;
                infoBar.Message      = string.Format(R.Get("CheckUpdate_Available"), tag);
                infoBar.ActionButton = updateBtn;
            }

            void ShowUpToDate()
            {
                infoBar.Severity     = InfoBarSeverity.Success;
                infoBar.Message      = R.Get("CheckUpdate_Latest");
                infoBar.ActionButton = releaseLink;
            }

            void ShowError()
            {
                infoBar.Severity     = InfoBarSeverity.Error;
                infoBar.Message      = R.Get("CheckUpdate_Error");
                infoBar.ActionButton = releaseLink;
            }

            // 初期表示。CachedLatestVersion の null だけでは「最新」と「未確認」を
            // 区別できないので、一度も確認できていないうちは断定しない。
            if (settings.CachedLatestVersion is { } cached
                && Version.TryParse(cached.TrimStart('v'), out var cachedVersion)
                && cachedVersion > currentVersion)
            {
                ShowAvailable(cached);
            }
            else if (settings.LastUpdateCheck is not null)
            {
                ShowUpToDate();
            }
            else
            {
                infoBar.Severity     = InfoBarSeverity.Informational;
                infoBar.Message      = R.Get("CheckUpdate_NotChecked");
                infoBar.ActionButton = releaseLink;
            }

            SetLastCheckedDescription(updateCard, settings.LastUpdateCheck);

            checkBtn.Click += async (_, _) =>
            {
                checkBtn.IsEnabled = false;
                ShowChecking();
                try
                {
                    if (_parent.FetchLatestVersionAsync is null) return;
                    var latest = await _parent.FetchLatestVersionAsync();
                    if (latest is null)
                    {
                        // 取得できなかったのに「最新です」と出すと、更新を見落とす。
                        AppLog.Debug("UpdateCheck(manual): 最新バージョンを取得できなかった");
                        ShowError();
                        return;
                    }

                    if (latest > currentVersion)
                    {
                        var tag = $"v{latest.ToString(3)}";
                        settings.CachedLatestVersion = tag;
                        ShowAvailable(tag);
                    }
                    else
                    {
                        settings.CachedLatestVersion = null;
                        ShowUpToDate();
                    }
                    settings.LastUpdateCheck = DateTimeOffset.Now;
                    SetLastCheckedDescription(updateCard, settings.LastUpdateCheck);
                    AppLog.Debug($"UpdateCheck(manual): current=v{currentVersion.ToString(3)} "
                               + $"latest=v{latest.ToString(3)} available={settings.CachedLatestVersion is not null}");
                    _parent.SaveSettingsOnly?.Invoke();
                    _parent.UpdateMenuBadge?.Invoke();
                }
                catch (Exception ex)
                {
                    AppLog.Error("UpdateCheck(manual)", ex);
                    ShowError();
                }
                finally
                {
                    checkBtn.IsEnabled = true;
                }
            };

            updateBtn.Click += async (_, _) =>
            {
                // ZIP 版は自己置き換えを行わず、リリースページへ誘導する (#328)
                if (!useWinget)
                {
                    AppLog.Debug("UpdateCheck: リリースページを開く");
                    OpenUri(new Uri(releaseUrl));
                    return;
                }

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

                AppLog.Debug("UpdateCheck: winget upgrade を起動してアプリを終了する");
                _parent.ExitAndRunWingetUpdate?.Invoke();
            };

            RootPanel.Children.Add(updateCard);
            RootPanel.Children.Add(infoBar);
        }

        /// <summary>「最終確認: ...」をカードの説明に出す。未確認なら何も出さない。</summary>
        private static void SetLastCheckedDescription(
            CommunityToolkit.WinUI.Controls.SettingsCard card, DateTimeOffset? lastCheck)
        {
            card.Description = lastCheck is null
                ? null
                : string.Format(R.Get("CheckUpdate_LastChecked"), lastCheck.Value.LocalDateTime);
        }

        /// <summary>親が持つ起動経路があればそれを使う。無ければ直接開く。</summary>
        private void OpenUri(Uri uri)
        {
            if (_parent?.LaunchUriAsync is not null)
                _parent.LaunchUriAsync(uri).FireAndForget("OpenReleasePage");
            else
                Windows.System.Launcher.LaunchUriAsync(uri).AsTask().FireAndForget("OpenReleasePage");
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
