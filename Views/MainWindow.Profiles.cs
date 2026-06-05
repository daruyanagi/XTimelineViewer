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

namespace XTimelineViewer.Views
{
    public sealed partial class MainWindow : Window
    {
        private void ManageProfilesMenuItem_Click(object _, RoutedEventArgs __)
        {
            var ownerHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var win = new ManageProfilesWindow(_profiles, ProfileBadgeColors, ownerHwnd,
                profileId => _configs.Count(c => c.ProfileId == profileId));
            var childTheme = ((FrameworkElement)Content).RequestedTheme;
            ((FrameworkElement)win.Content).RequestedTheme = childTheme;
            ApplyTitleBarTheme(win, childTheme);
            win.ProfilesChanged += (__, args) =>
            {
                foreach (var change in args.Changes)
                {
                    var p = _profiles.FirstOrDefault(p => p.Id == change.ProfileId);
                    if (p == null) continue;
                    p.Name = change.NewName;
                    p.BadgeColorIndex = change.NewColorIndex;
                    p.BadgeText = change.NewBadgeText;
                }
                SaveProfiles();
                RefreshAllProfileBadges();
            };
            win.ProfileCreated += (__, profile) =>
            {
                SaveProfiles();
                RefreshAllProfileBadges();
                Debug.WriteLine($"[Profile] Saved to profiles.json: Id={profile.Id}, Name={profile.Name}");
            };
            win.ProfileDeleteRequested += async (__, profileId) =>
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
                Debug.WriteLine($"[Profile] Deleted: {profileId}");
            };
            win.Activate();
        }

        private void RefreshAllProfileBadges()
        {
            for (int i = 0; i < _configs.Count && i < TimelinePanel.Children.Count; i++)
            {
                var pane = (Grid)TimelinePanel.Children[i];
                var headerGrid = pane.Children.OfType<Grid>().FirstOrDefault();
                if (headerGrid == null) continue;
                var oldBadge = headerGrid.Children.OfType<Border>()
                    .FirstOrDefault(b => Grid.GetColumn(b) == 1);
                if (oldBadge != null)
                    headerGrid.Children.Remove(oldBadge);
                var newBadge = CreateProfileBadge(_configs[i].ProfileId);
                Grid.SetColumn(newBadge, 1);
                headerGrid.Children.Add(newBadge);
            }
        }

        private void RemoveTimelinesForProfile(string profileId)
        {
            var indices = new List<int>();
            for (int i = 0; i < _configs.Count; i++)
                if (_configs[i].ProfileId == profileId)
                    indices.Add(i);

            for (int i = indices.Count - 1; i >= 0; i--)
            {
                var idx = indices[i];
                if (idx >= TimelinePanel.Children.Count) continue;
                var pane = (Grid)TimelinePanel.Children[idx];
                var wv = pane.Children.OfType<WebView2>().FirstOrDefault();
                if (wv != null)
                {
                    CleanupWebView(wv);
                }
                var headerGrid = pane.Children.OfType<Grid>().FirstOrDefault();
                if (headerGrid != null)
                {
                    _homeHeaderGrids.Remove(headerGrid);
                    if (_focusedHeaderGrid == headerGrid)
                        _focusedHeaderGrid = null;
                }
                _paneToSetFocus.Remove(pane);
                TimelinePanel.Children.RemoveAt(idx);
                _configs.RemoveAt(idx);
            }

            if (_hardReloadUiUpdaters.Count == 0)
            {
                _hardReloadUiTimer?.Stop();
                _hardReloadUiTimer = null;
            }
            if (_focusedHeaderGrid == null)
                foreach (var r in _headerRefreshers) r();

            ViewModel.HasTimelines = TimelinePanel.Children.Count > 0;
        }

        private static readonly Color[] ProfileBadgeColors =
        [
            Color.FromArgb(255,  56, 142, 60),   // green
            Color.FromArgb(255, 211,  47,  47),  // red
            Color.FromArgb(255,  25, 118, 210),  // blue
            Color.FromArgb(255, 156,  39, 176),  // purple
            Color.FromArgb(255, 245, 124,   0),  // orange
            Color.FromArgb(255,   0, 151, 167),  // teal
            Color.FromArgb(255, 121,  85,  72),  // brown
            Color.FromArgb(255,  63,  81, 181),  // indigo
        ];

        private static Color GetProfileColor(string profileId)
        {
            var hash = Math.Abs(profileId.GetHashCode());
            return ProfileBadgeColors[hash % ProfileBadgeColors.Length];
        }

        private Color GetProfileColor(ProfileConfig? profile, string profileId)
        {
            if (profile?.BadgeColorIndex is int idx && idx >= 0 && idx < ProfileBadgeColors.Length)
                return ProfileBadgeColors[idx];
            return GetProfileColor(profileId);
        }

        private Border CreateProfileBadge(string profileId)
        {
            var showBadge = _profiles.Count > 1 && profileId != "default";
            var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
            var name = profile?.Name ?? profileId;
            var badgeText = profile?.BadgeText is { Length: > 0 } custom
                ? custom
                : (name.Length > 3 ? name[..3] : name);
            var color = GetProfileColor(profile, profileId);

            return new Border
            {
                Background    = new SolidColorBrush(color),
                CornerRadius  = new CornerRadius(4),
                Padding       = new Thickness(4, 1, 4, 1),
                Margin        = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility    = showBadge ? Visibility.Visible : Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text       = badgeText,
                    FontSize   = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                }
            };
        }
    }
}
