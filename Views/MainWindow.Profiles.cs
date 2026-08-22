using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;

using XTimelineViewer.Models;

using XTimelineViewer.Views.Controls;

namespace XTimelineViewer.Views
{
    public sealed partial class MainWindow : Window
    {
        private void RefreshAllProfileBadges()
        {
            // 以前は Border ごと作り直して列 2 へ入れ直していたため、
            // 列番号が生成側とこちらの 2 か所に手書きされていた（#337 / #341）。
            // 器は TimelinePane.xaml に固定され、ここは中身を更新するだけで済む。
            foreach (var pane in TimelinePanel.Children.OfType<TimelinePane>())
                ApplyProfileBadge(pane);
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
                var pane = (TimelinePane)TimelinePanel.Children[idx];
                if (_enlargedPane == pane) RestorePaneSize();  // 拡大中のペイン削除に備える（#287）
                CleanupWebView(pane.WebView);
                if (_focusedPane == pane)
                    _focusedPane = null;
                // ⚙ ダイアログからの削除（MainWindow.Timeline.cs）と同じ後始末を行うこと。
                // 以前は抜けているものがあり、消えたペインへの参照が残っていた（#362）。
                TimelinePanel.Children.RemoveAt(idx);
                _configs.RemoveAt(idx);
            }

            if (_hardReloadUiUpdaters.Count == 0)
            {
                _hardReloadUiTimer?.Stop();
                _hardReloadUiTimer = null;
            }
            if (_focusedPane == null)
                RefreshPaneThemes();

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

        /// <summary>
        /// ペインのプロファイルバッジを現在の設定に合わせて更新する。
        /// 以前は Border を新規作成して差し替えており、列番号の手書きが
        /// 生成側と再生成側の 2 か所に分かれていた（#337）。
        /// </summary>
        private void ApplyProfileBadge(TimelinePane pane)
        {
            var profileId = pane.Config.ProfileId;
            var showBadge = _profiles.Count > 1 && profileId != "default";
            var profile   = _profiles.FirstOrDefault(p => p.Id == profileId);
            var name      = profile?.Name ?? profileId;
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

            pane.SetProfileBadge(badgeText, bg, fg, bordered: hc, visible: showBadge);
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
