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
using XTimelineViewer.Models;
using XTimelineViewer.Services;

namespace XTimelineViewer.Views
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

        private void OpenSettingsWindow_Click(object _, RoutedEventArgs __)
            => OpenSettingsWindow();

        private void OpenSettingsWindow(string initialPage = "General")
        {
            var ownerHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var settingsFolder = Path.GetDirectoryName(SettingsFilePath)!;
            var settingsWin = new SettingsWindow(ownerHwnd, _appSettings, settingsFolder);

            // 拡張機能情報とコールバックを設定
            settingsWin.Extensions = _loadedExtensions;
            settingsWin.OpenExtensionSettingsAsync = (info, xamlRoot) =>
                ShowExtensionSettingsDialogAsync(info, xamlRoot, LaunchUriByEdgeProfileAsync);
            settingsWin.LaunchUriAsync = LaunchUriByEdgeProfileAsync;

            // プロファイル情報とコールバックを設定
            settingsWin.Profiles = _profiles;
            settingsWin.BadgeColors = ProfileBadgeColors;
            settingsWin.GetTimelineCount = profileId => _configs.Count(c => c.ProfileId == profileId);
            settingsWin.ProfilesModified = () => { SaveProfiles(); RefreshAllProfileBadges(); };
            settingsWin.DeleteProfileAsync = async profileId =>
            {
                RemoveTimelinesForProfile(profileId);
                _profileEnvs.Remove(profileId);
                var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
                if (profile != null) _profiles.Remove(profile);
                if (_profiles.Count == 0)
                    _profiles.Add(new ProfileConfig { Id = "default", Name = "Default" });
                SaveProfiles();
                try
                {
                    var folder = Path.Combine(GetProfilesDataDir(), profileId);
                    if (Directory.Exists(folder))
                        Directory.Delete(folder, recursive: true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Profile] Failed to delete profile folder: {ex.Message}");
                }
                await SaveTimelinesAsync();
                RefreshAllProfileBadges();
            };
            settingsWin.OnProfileCreated = _ => { SaveProfiles(); RefreshAllProfileBadges(); };

            // About ページ情報とコールバックを設定
            string edgeVer;
            try
            {
                edgeVer = CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch
            {
                edgeVer = _profileEnvs.Values.FirstOrDefault()?.BrowserVersionString
                          ?? R.Get("Version_Unknown");
            }
            settingsWin.EdgeVersion = edgeVer;
            settingsWin.HasWinget = !PackageContext.IsPackaged && FindWinget() is not null;
            settingsWin.FetchLatestReleaseAsync = FetchLatestReleaseAsync;
            settingsWin.CheckIsUpdateAvailable = IsUpdateAvailable;
            settingsWin.SaveSettingsOnly = SaveSettings;
            settingsWin.UpdateMenuBadge = UpdateMenuUpdateBadge;
            settingsWin.ExitAndRunWingetUpdate = () =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = "cmd.exe",
                    Arguments       = "/c timeout /t 2 /nobreak > nul && winget upgrade daruyanagi.XTimelineViewer",
                    UseShellExecute = true,
                });
                Application.Current.Exit();
            };

            // 親ウィンドウのテーマを引き継ぐ
            var theme = ((FrameworkElement)Content).RequestedTheme;
            settingsWin.ApplyTheme(theme);

            // 設定変更を即時反映
            settingsWin.SettingsChanged += () =>
            {
                SaveSettings();
                ApplySavedTheme();
                ApplyAutoActivateTimer();

                // WebView のタイムスタンプ設定を即時反映
                var tsFlag = _appSettings.OpenTimestampInBrowser ? "true" : "false";
                foreach (var wv in _webViews)
                    if (wv.CoreWebView2 is not null)
                        _ = wv.CoreWebView2.ExecuteScriptAsync(
                            $"window._xtvOpenTimestampInBrowser = {tsFlag};");

                // 言語変更の即時反映
                var locale = _appSettings.Language == "system" ? null : _appSettings.Language;
                R.Reload(locale);
                RefreshUIText();
                settingsWin.RefreshNavText();
            };

            // 初期ページを選択
            if (initialPage != "General")
                settingsWin.SelectPage(initialPage);

            settingsWin.Activate();
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

            // ── 外部ブラウザー ────────────────────────────────────────────────
            panel.Children.Add(new NavigationViewItemSeparator { Margin = new Thickness(0, 12, 0, 8) });
            panel.Children.Add(new TextBlock
            {
                Text       = R.Get("Section_ExternalBrowser"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            var browserValues = new[] { "system", "edge" };
            var browserCombo = new ComboBox
            {
                ItemsSource   = new List<string> { R.Get("Browser_System"), "Microsoft Edge" },
                SelectedIndex = _appSettings.ExternalBrowser == "edge" ? 1 : 0,
                MinWidth      = 200
            };

            var edgeProfiles = EdgeService.EnumerateProfiles();
            var profileCombo = new ComboBox
            {
                MinWidth  = 200,
                IsEnabled = _appSettings.ExternalBrowser == "edge" && edgeProfiles.Count > 0
            };

            // Edge 未インストール時はプルダウンに「Edge が見つかりません」を表示
            if (edgeProfiles.Count == 0)
            {
                profileCombo.ItemsSource   = new List<string> { R.Get("Browser_EdgeNotFound") };
                profileCombo.SelectedIndex = 0;
                profileCombo.IsEnabled     = false;
            }
            else
            {
                var profileDisplayNames = new List<string>();
                int selectedProfileIdx = 0;
                for (int i = 0; i < edgeProfiles.Count; i++)
                {
                    var p = edgeProfiles[i];
                    var detail = p.UserName.Length > 0 ? p.UserName : p.Directory;
                    profileDisplayNames.Add($"{p.DisplayName}  ({detail})");
                    if (p.Directory == _appSettings.EdgeProfileDirectory)
                        selectedProfileIdx = i;
                }
                profileCombo.ItemsSource   = profileDisplayNames;
                profileCombo.SelectedIndex = selectedProfileIdx;
            }

            browserCombo.SelectionChanged += (_, _) =>
            {
                var isEdge = browserCombo.SelectedIndex == 1;
                profileCombo.IsEnabled = isEdge && edgeProfiles.Count > 0;
            };

            panel.Children.Add(MakeRow(R.Get("Settings_ExternalBrowser"), browserCombo));
            panel.Children.Add(MakeRow(R.Get("Settings_EdgeProfile"), profileCombo));

            var dlg = new ContentDialog
            {
                Title             = R.Get("AppSettings_Title"),
                Content           = panel,
                PrimaryButtonText = R.Get("Button_Save"),
                CloseButtonText   = R.Get("Button_Cancel"),
                DefaultButton     = ContentDialogButton.Primary,
                XamlRoot          = Content.XamlRoot,
            };

                        if (await ShowDialogAsync(dlg) == ContentDialogResult.Primary)
            {
                _appSettings.Theme = themeCombo.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "Default" };
                _appSettings.OpenComposerInBrowser = openPostToggle.IsOn;
                _appSettings.OpenTimestampInBrowser = openTimestampToggle.IsOn;
                _appSettings.AutoActivateMinutes   = (int)Math.Clamp(autoActivateBox.Value, 0, 60);
                _appSettings.ShowAutoActivateLabel = showAutoActivateLabelToggle.IsOn;
                _appSettings.ExternalBrowser      = browserValues[Math.Max(0, Math.Min(browserCombo.SelectedIndex, browserValues.Length - 1))];
                if (edgeProfiles.Count > 0 && profileCombo.SelectedIndex >= 0 && profileCombo.SelectedIndex < edgeProfiles.Count)
                    _appSettings.EdgeProfileDirectory = edgeProfiles[profileCombo.SelectedIndex].Directory;

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

        private void AboutMenuItem_Click(object _, RoutedEventArgs __)
            => OpenSettingsWindow("About");
    }
}
