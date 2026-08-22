using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using XTimelineViewer.Models;
using XTimelineViewer.Services;

using XTimelineViewer.Views.Controls;

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
                // 生成済み（完了済み）の環境からバージョンを拾う。#339 で Task キャッシュに
                // 変えたため、まだ完了していない/失敗した Task は対象外にする。
                edgeVer = _profileEnvs.Values
                              .FirstOrDefault(t => t.IsCompletedSuccessfully)?.Result.BrowserVersionString
                          ?? R.Get("Version_Unknown");
            }
            settingsWin.EdgeVersion = edgeVer;
            settingsWin.HasWinget = !PackageContext.IsPackaged && FindWinget() is not null;
            settingsWin.FetchLatestVersionAsync = FetchLatestVersionAsync;
            settingsWin.SaveSettingsOnly = SaveSettings;
            settingsWin.UpdateMenuBadge = UpdateMenuUpdateBadge;
            // ZIP 版の自前更新（#328）。インストール先は実行中の exe が居る場所。
            settingsWin.RunZipUpdateAsync = (progress, ct) => ZipUpdateRunner.RunAsync(
                _downloadHttp,
                AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar),
                progress, ct);
            settingsWin.ExitApp = () => Application.Current.Exit();

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
                WarmUpComposeAsync().FireAndForget(nameof(WarmUpComposeAsync));  // 投稿プリロードの ON/OFF を即時反映（#244 案B）
                // 画像(#287)・動画(#289)拡大トグルが両方 OFF になったら即座に復元する
                if (!_appSettings.MediaEnlargeEnabled && !_appSettings.VideoEnlargeEnabled)
                    RestorePaneSize();

                // WebView のタイムスタンプ設定を即時反映
                var tsFlag = _appSettings.OpenTimestampInBrowser ? "true" : "false";
                foreach (var pane in Panes)
                    if (pane.WebView.CoreWebView2 is not null)
                        pane.WebView.CoreWebView2.ExecuteScriptAsync(
                            $"window._xtvOpenTimestampInBrowser = {tsFlag};").AsTask().FireAndForget("ExecuteScript");

                // メディア拡大ボタン（#293）の ON/OFF を各ペインへ即時反映
                foreach (var pane in Panes)
                    if (pane.WebView.CoreWebView2 is not null)
                        ApplyMediaOverlayButtonAsync(pane.WebView).FireAndForget(nameof(ApplyMediaOverlayButtonAsync));

                // 「直前のリポストを検索」（#315）の ON/OFF を各ペインへ即時反映
                foreach (var pane in Panes)
                    if (pane.WebView.CoreWebView2 is not null)
                        ApplyPriorRepostSearchAsync(pane.WebView).FireAndForget(nameof(ApplyPriorRepostSearchAsync));

                // ホーム自動更新（#207）の ON/OFF・間隔を各ホームペインへ即時反映し、インジケーターも更新
                // 以前は _autoLoadIndicators を「ホームペイン集合」の代用にし、
                // そこから型で WebView2 を探していた（#345）。
                foreach (var pane in TimelinePanel.Children.OfType<TimelinePane>())
                {
                    if (pane.WebView.CoreWebView2 is not null)
                        ApplyHomeAutoLoadAsync(pane.WebView).FireAndForget(nameof(ApplyHomeAutoLoadAsync));
                    UpdateAutoLoadIndicator(pane, _appSettings.HomeAutoLoadEnabled ? "running" : "off");
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
