using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
                UpdateHasNamedProfiles();
            };
            settingsWin.OnProfileCreated = _ => { SaveProfiles(); RefreshAllProfileBadges(); UpdateHasNamedProfiles(); };

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
            settingsWin.FetchWingetLatestVersionAsync = FetchWingetLatestVersionAsync;
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
                UpdateThemeRadioState();
                ApplyAutoActivateTimer();
                _ = WarmUpComposeAsync();  // 投稿プリロードの ON/OFF を即時反映（#244 案B）

                // WebView のタイムスタンプ設定を即時反映
                var tsFlag = _appSettings.OpenTimestampInBrowser ? "true" : "false";
                foreach (var wv in _webViews)
                    if (wv.CoreWebView2 is not null)
                        _ = wv.CoreWebView2.ExecuteScriptAsync(
                            $"window._xtvOpenTimestampInBrowser = {tsFlag};");

                // ホーム自動更新（#207）の ON/OFF・間隔を各ホームペインへ即時反映し、インジケーターも更新
                foreach (var (homePane, _) in _autoLoadIndicators)
                {
                    if (homePane.Children.OfType<Microsoft.UI.Xaml.Controls.WebView2>()
                            .FirstOrDefault() is { CoreWebView2: not null } hwv)
                        _ = ApplyHomeAutoLoadAsync(hwv);
                    UpdateAutoLoadIndicator(homePane, _appSettings.HomeAutoLoadEnabled ? "running" : "off");
                }

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

    }
}
