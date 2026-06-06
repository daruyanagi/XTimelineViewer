using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;

using XTimelineViewer.Models;

namespace XTimelineViewer.Views
{
    public sealed partial class MainWindow : Window
    {
        private void ManageProfilesMenuItem_Click(object _, RoutedEventArgs __)
            => OpenSettingsWindow("Profiles");

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
