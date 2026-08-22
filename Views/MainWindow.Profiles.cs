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
        private void RefreshAllProfileBadges()
        {
            for (int i = 0; i < _configs.Count && i < TimelinePanel.Children.Count; i++)
            {
                var pane = (Grid)TimelinePanel.Children[i];
                var headerGrid = pane.Children.OfType<Grid>().FirstOrDefault();
                if (headerGrid == null) continue;
                // バッジは列 2（列構成: 0 番号 / 1 種別アイコン / 2 バッジ / 3 URL / 4 ボタン）。
                // #337 で AddTimeline 側を 1 → 2 に直したときここを見落としており、
                // 古いバッジを見つけられず重複していた（プロファイル変更時に発現）。
                var oldBadge = headerGrid.Children.OfType<Border>()
                    .FirstOrDefault(b => Grid.GetColumn(b) == 2);
                if (oldBadge != null)
                    headerGrid.Children.Remove(oldBadge);
                var newBadge = CreateProfileBadge(_configs[i].ProfileId);
                Grid.SetColumn(newBadge, 2);
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
                if (_enlargedPane == pane) RestorePaneSize();  // 拡大中のペイン削除に備える（#287）
                var wv = pane.Children.OfType<WebView2>().FirstOrDefault();
                if (wv != null)
                {
                    CleanupWebView(wv);
                }
                var headerGrid = pane.Children.OfType<Grid>().FirstOrDefault();
                if (headerGrid != null)
                {
                    if (_focusedHeaderGrid == headerGrid)
                        _focusedHeaderGrid = null;
                }
                _paneToSetFocus.Remove(pane);
                _paneUrlUpdaters.Remove(_configs[idx]);
                _paneToConfig.Remove(pane);
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

            // 番号バッジは表示順に依存するので、削除後は振り直す（#359）。
            // ⚙ ダイアログからの削除では呼んでいたが、こちらの経路では抜けており、
            // Ctrl+数字（位置で判定）とバッジの表示が食い違っていた。
            RefreshTimelineNumbers();

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

        // string.GetHashCode() は .NET (Core) ではプロセスごとにランダム化されるため、
        // 起動のたびに同じ profileId が別の色になる問題があった (#160)。
        // FNV-1a 風の決定的ハッシュで安定したインデックスを返す。
        internal static int StableIndex(string s, int modulo)
        {
            if (modulo <= 0) return 0;
            unchecked
            {
                int hash = 17;
                foreach (char c in s) hash = hash * 31 + c;
                return (int)((uint)hash % (uint)modulo);
            }
        }

        private static Color GetProfileColor(string profileId)
            => ProfileBadgeColors[StableIndex(profileId, ProfileBadgeColors.Length)];

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

            // コントラストテーマ中は固定色を使わない（#341）。
            // バッジはプロファイル名の先頭数文字を表示しているので、
            // 色を落としても識別は成立する。枠線でバッジだと分かるようにする。
            bool hc = IsHighContrast();
            var bg = hc ? (Brush)Application.Current.Resources["SystemColorWindowColorBrush"]
                        : new SolidColorBrush(color);
            var fg = hc ? (Brush)Application.Current.Resources["SystemColorWindowTextColorBrush"]
                        : new SolidColorBrush(Microsoft.UI.Colors.White);

            return new Border
            {
                Background      = bg,
                BorderBrush     = hc ? fg : null,
                BorderThickness = new Thickness(hc ? 1 : 0),
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
                    Foreground = fg,
                }
            };
        }

        // メニューから新規プロファイル作成ウィンドウをモーダルで開く (#157)
        private void NewProfileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            AddProfileWindow.ShowModal(this, profile =>
            {
                _profiles.Add(profile);
                SaveProfiles();
                RefreshAllProfileBadges();
                UpdateHasNamedProfiles();
            });
        }
    }
}
