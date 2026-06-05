using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.UI;
using XTimelineViewer.Services;

namespace XTimelineViewer
{
    public sealed partial class MainWindow : Window
    {
        private void LoadSettings()
        {
            _appSettings = SettingsService.LoadSettings(SettingsFilePath);
        }

        private void SaveSettings()
        {
            SettingsService.SaveSettings(SettingsFilePath, _appSettings);
        }

        private void LoadProfiles()
        {
            _profiles = SettingsService.LoadProfiles(ProfilesFilePath);
            if (_profiles.Count == 0)
            {
                _profiles.Add(new ProfileConfig { Id = "default", Name = "Default" });
                SaveProfiles();
            }
        }

        private void SaveProfiles()
        {
            SettingsService.SaveProfiles(ProfilesFilePath, _profiles);
        }

        private void CleanupOrphanedProfiles()
        {
            SettingsService.CleanupOrphanedProfileFolders(
                GetProfilesDataDir(),
                _profiles.Select(p => p.Id));
        }

        private async void AppSettingsMenuItem_Click(object _, RoutedEventArgs __)
        {
            var themeCombo = new ComboBox
            {
                ItemsSource   = new List<string> { R.Get("Theme_System"), R.Get("Theme_Light"), R.Get("Theme_Dark") },
                SelectedIndex = _appSettings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 },
                MinWidth      = 140
            };
            AutomationProperties.SetAutomationId(themeCombo, "ThemeComboBox");

            var langValues = new[] { "system", "ja-JP", "en-US" };
            var langIdx    = Array.IndexOf(langValues, _appSettings.Language);
            var langCombo  = new ComboBox
            {
                ItemsSource   = new List<string> { R.Get("Language_System"), R.Get("Language_JA"), R.Get("Language_EN") },
                SelectedIndex = langIdx < 0 ? 0 : langIdx,
                MinWidth      = 140
            };
            AutomationProperties.SetAutomationId(langCombo, "LanguageComboBox");

            var openFolderBtn = new Button { Content = R.Get("Button_OpenFolder") };
            openFolderBtn.Click += async (_, _) =>
            {
                var folder = Path.GetDirectoryName(SettingsFilePath)!;
                Directory.CreateDirectory(folder);
                await Windows.System.Launcher.LaunchFolderPathAsync(folder);
            };

            static Grid MakeRow(string label, FrameworkElement control, Thickness? margin = null)
            {
                var g = new Grid { Margin = margin ?? new Thickness(0, 6, 0, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var lbl = new TextBlock
                {
                    Text = label,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(lbl, 0);
                Grid.SetColumn(control, 1);
                g.Children.Add(lbl);
                g.Children.Add(control);
                return g;
            }

            var panel = new StackPanel { MinWidth = 400 };
            panel.Children.Add(MakeRow(R.Get("Settings_Theme"), themeCombo, new Thickness(0)));
            panel.Children.Add(MakeRow(R.Get("Settings_Language"), langCombo));
            panel.Children.Add(MakeRow(R.Get("Settings_ExportFolder"), openFolderBtn));
            panel.Children.Add(new TextBlock
            {
                Text                   = Path.GetDirectoryName(SettingsFilePath),
                TextWrapping           = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Opacity                = 0.6,
                FontSize               = 12,
                Margin                 = new Thickness(0, 2, 0, 0),
            });
            panel.Children.Add(new NavigationViewItemSeparator { Margin = new Thickness(0, 12, 0, 8) });
            panel.Children.Add(new TextBlock
            {
                Text       = R.Get("Section_Experimental"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            var separateEnvToggle = new ToggleSwitch
            {
                IsOn              = false,
                IsEnabled         = false, // 廃止予定 (#17)
                OnContent         = R.Get("Toggle_On"),
                OffContent        = R.Get("Toggle_Off"),
                Margin              = new Thickness(12, 0, 0, 0),
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            panel.Children.Add(MakeRow(R.Get("Settings_SeparateCompose"), separateEnvToggle));

            var openPostToggle = new ToggleSwitch
            {
                IsOn                = _appSettings.OpenComposerInBrowser,
                OnContent           = R.Get("Toggle_On"),
                OffContent          = R.Get("Toggle_Off"),
                Margin              = new Thickness(12, 0, 0, 0),
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            panel.Children.Add(MakeRow(R.Get("Settings_OpenComposerInBrowser"), openPostToggle));

            var openTimestampToggle = new ToggleSwitch
            {
                IsOn                = _appSettings.OpenTimestampInBrowser,
                OnContent           = R.Get("Toggle_On"),
                OffContent          = R.Get("Toggle_Off"),
                Margin              = new Thickness(12, 0, 0, 0),
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            panel.Children.Add(MakeRow(R.Get("Settings_OpenTimestampInBrowser"), openTimestampToggle));

            var autoActivateBox = new NumberBox
            {
                Value                   = _appSettings.AutoActivateMinutes,
                Minimum                 = 0,
                Maximum                 = 60,
                SmallChange             = 1,
                LargeChange             = 5,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                Width                   = 160,
            };
            panel.Children.Add(MakeRow(R.Get("Settings_AutoActivate"), autoActivateBox));

            var showAutoActivateLabelToggle = new ToggleSwitch
            {
                IsOn                = _appSettings.ShowAutoActivateLabel,
                OnContent           = R.Get("Toggle_On"),
                OffContent          = R.Get("Toggle_Off"),
                Margin              = new Thickness(12, 0, 0, 0),
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            panel.Children.Add(MakeRow(R.Get("Settings_ShowAutoActivateLabel"), showAutoActivateLabelToggle));

            var dlg = new ContentDialog
            {
                Title             = R.Get("AppSettings_Title"),
                Content           = panel,
                PrimaryButtonText = R.Get("Button_Save"),
                CloseButtonText   = R.Get("Button_Cancel"),
                DefaultButton     = ContentDialogButton.Primary,
                XamlRoot          = Content.XamlRoot,
                RequestedTheme    = ((FrameworkElement)Content).ActualTheme
            };

                        if (await ShowDialogAsync(dlg) == ContentDialogResult.Primary)
            {
                _appSettings.Theme = themeCombo.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "Default" };
                _appSettings.OpenComposerInBrowser = openPostToggle.IsOn;
                _appSettings.OpenTimestampInBrowser = openTimestampToggle.IsOn;
                _appSettings.AutoActivateMinutes   = (int)Math.Clamp(autoActivateBox.Value, 0, 60);
                _appSettings.ShowAutoActivateLabel = showAutoActivateLabelToggle.IsOn;

                var newLang    = langValues[Math.Max(0, Math.Min(langCombo.SelectedIndex, langValues.Length - 1))];
                var langChanged = newLang != _appSettings.Language;
                _appSettings.Language = newLang;

                SaveSettings();
                ApplySavedTheme();

                var tsFlag = _appSettings.OpenTimestampInBrowser ? "true" : "false";
                foreach (var wv in _webViews)
                    if (wv.CoreWebView2 is not null)
                        await wv.CoreWebView2.ExecuteScriptAsync(
                            $"window._xtvOpenTimestampInBrowser = {tsFlag};");

                ApplyAutoActivateTimer();

                if (langChanged)
                {
                    // 再起動なしで即時反映する (#117)。
                    // "system" の場合はシステム言語にフォールバックさせるため null を渡す。
                    var locale = newLang == "system" ? null : newLang;
                    R.Reload(locale);
                    RefreshUIText();
                }
            }
        }

        private async void AboutMenuItem_Click(object _, RoutedEventArgs __)
        {
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version!;
            var versionStr     = currentVersion.ToString(3);

            var edgeChannel = R.Get("EdgeChannel_Runtime");
            string edgeVersion;
            try
            {
                edgeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch
            {
                edgeVersion = _profileEnvs.Values.FirstOrDefault()?.BrowserVersionString
                              ?? R.Get("Version_Unknown");
            }
            var versionInfoText = $"XTimelineViewer v{versionStr}\r\n{edgeChannel} {edgeVersion}";

            var issueBody = Uri.EscapeDataString(
                $"- {R.Get("IssueLabel_AppVersion")}: v{versionStr}\n" +
                $"- {R.Get("IssueLabel_EdgeVersion")}: {edgeChannel} {edgeVersion}\n" +
                $"- {R.Get("IssueLabel_Symptoms")}:\n");
            var issueUrl    = $"https://github.com/daruyanagi/XTimelineViewer/issues/new?labels=bug&title=&body={issueBody}";
            var repoUrl     = "https://github.com/daruyanagi/XTimelineViewer";
            var fallbackUrl = repoUrl + "/releases/latest";

            // ── Header ────────────────────────────────────────────────────────
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
                Margin      = new Thickness(0, 0, 0, 8),
            };
            if (File.Exists(iconPath))
                titleRow.Children.Add(new Microsoft.UI.Xaml.Controls.Image
                {
                    Source            = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconPath)),
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
                        new FontIcon { Glyph = "", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14 },
                        new TextBlock { Text = R.Get("Button_Copy") },
                    }
                },
                Margin = new Thickness(0, 4, 8, 0),
            };
            copyBtn.Click += (_, _) =>
            {
                var dp = new DataPackage();
                dp.SetText(versionInfoText);
                Clipboard.SetContent(dp);
            };

            // ── Update check ─────────────────────────────────────────────────
            string? latestReleaseUrl = null;

            var statusText = new TextBlock { FontSize = 13, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };

            bool hasWinget = !PackageContext.IsPackaged && FindWinget() is not null;
            var updateBtnLabel = PackageContext.IsPackaged
                ? R.Get("CheckUpdate_Download_Store")
                : hasWinget
                    ? R.Get("CheckUpdate_Download_Winget")
                    : R.Get("CheckUpdate_Download_GitHub");
            var updateBtn = new Button { Content = updateBtnLabel, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };

            // キャッシュがあれば初期表示
            if (_appSettings.CachedLatestVersion is { } cached
                && Version.TryParse(cached.TrimStart('v'), out var cachedVersion)
                && IsUpdateAvailable(currentVersion, cachedVersion))
            {
                latestReleaseUrl = $"{repoUrl}/releases/tag/{cached}";
                statusText.Text       = string.Format(R.Get("CheckUpdate_Available"), cached);
                statusText.Visibility = Visibility.Visible;
                updateBtn.Visibility  = Visibility.Visible;
            }

            var checkBtn = new Button { Content = R.Get("CheckUpdate_Btn"), Margin = new Thickness(0, 4, 0, 0) };
            checkBtn.Click += async (_, _) =>
            {
                checkBtn.IsEnabled    = false;
                statusText.Text       = R.Get("CheckUpdate_Checking");
                statusText.Visibility = Visibility.Visible;
                updateBtn.Visibility  = Visibility.Collapsed;
                try
                {
                    var (latest, tag, freshUrl) = await FetchLatestReleaseAsync();
                    latestReleaseUrl = freshUrl;
                    _appSettings.LastUpdateCheck = DateTime.UtcNow.ToString("O");
                    if (IsUpdateAvailable(currentVersion, latest))
                    {
                        _appSettings.CachedLatestVersion = tag;
                        statusText.Text      = string.Format(R.Get("CheckUpdate_Available"), tag);
                        updateBtn.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        _appSettings.CachedLatestVersion = null;
                        statusText.Text = R.Get("CheckUpdate_Latest");
                    }
                    SaveSettings();
                    UpdateMenuUpdateBadge();
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

            ContentDialog dlg = null!;

            updateBtn.Click += async (_, _) =>
            {
                var url = latestReleaseUrl ?? fallbackUrl;
                if (PackageContext.IsPackaged)
                {
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-windows-store://pdp/?ProductId=9P308HB5BLJ1"));
                }
                else if (FindWinget() is not null)
                {
                    dlg.Hide();
                    await SelfUpdateViaWingetAsync(url);
                }
                else
                {
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                }
            };

            var updateRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            updateRow.Children.Add(copyBtn);
            updateRow.Children.Add(checkBtn);

            var header = new StackPanel { Spacing = 0, Margin = new Thickness(0, 0, 0, 4) };
            header.Children.Add(titleRow);
            header.Children.Add(updateRow);
            header.Children.Add(statusText);
            header.Children.Add(updateBtn);

            // ── Section helper ────────────────────────────────────────────────
            static StackPanel MakeSection(string title)
            {
                var s = new StackPanel { Spacing = 4, Margin = new Thickness(0, 12, 0, 0) };
                s.Children.Add(new NavigationViewItemSeparator { Margin = new Thickness(0, 0, 0, 8) });
                s.Children.Add(new TextBlock
                {
                    Text       = title,
                    FontSize   = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Opacity    = 0.8,
                    Margin     = new Thickness(0, 0, 0, 4),
                });
                return s;
            }

            HyperlinkButton MakeLink(string text, string url) => new HyperlinkButton
            {
                NavigateUri = new Uri(url),
                Padding     = new Thickness(0),
                Content     = new StackPanel
                {
                    Orientation       = Orientation.Horizontal,
                    Spacing           = 4,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children          =
                    {
                        new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
                        new FontIcon
                        {
                            Glyph             = "",
                            FontFamily        = new FontFamily("Segoe Fluent Icons"),
                            FontSize          = 10,
                            Opacity           = 0.6,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    }
                },
            };

            var linksSection = MakeSection(R.Get("About_Links"));
            linksSection.Children.Add(MakeLink(R.Get("About_Repository"), repoUrl));
            linksSection.Children.Add(MakeLink(R.Get("Button_ReportIssue"), issueUrl));

            var acksSection = MakeSection(R.Get("About_Acknowledgements"));
            acksSection.Children.Add(MakeLink("TwitterTimelineLoader",
                "https://chromewebstore.google.com/detail/twittertimelineloader/ipmgjpmedafkmmadinmeoannpofakpbh"));

            var licenseSection = MakeSection(R.Get("About_License"));
            licenseSection.Children.Add(new TextBlock { Text = "MIT License", IsTextSelectionEnabled = true, FontSize = 13 });

            var componentsSection = MakeSection(R.Get("About_Components"));
            componentsSection.Children.Add(new TextBlock
            {
                Text                   = $"{edgeChannel}  {edgeVersion}",
                IsTextSelectionEnabled = true,
                FontSize               = 13,
                Opacity                = 0.8,
            });

            var panel = new StackPanel { MinWidth = 320 };
            panel.Children.Add(header);
            panel.Children.Add(linksSection);
            panel.Children.Add(acksSection);
            panel.Children.Add(licenseSection);
            panel.Children.Add(componentsSection);

            dlg = new ContentDialog
            {
                Title           = R.Get("About_Title"),
                Content         = panel,
                CloseButtonText = R.Get("Button_Close"),
                XamlRoot        = Content.XamlRoot,
                RequestedTheme  = ((FrameworkElement)Content).ActualTheme,
            };
            await ShowDialogAsync(dlg);
        }
    }
}
