using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using Windows.UI;
using XTimelineViewer.Models;

namespace XTimelineViewer.Views.Settings
{
    public sealed partial class ProfilesPage : Page
    {
        private SettingsWindow? _parent;

        public ProfilesPage()
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
            PageTitle.Text = R.Get("Nav_Profiles");
            AddProfileLabel.Text = R.Get("Menu_NewProfile");

            // 既存のプロファイルカードを削除（AddProfileBtn は残す）
            for (int i = RootPanel.Children.Count - 2; i >= 1; i--)
                RootPanel.Children.RemoveAt(i);

            var profiles = _parent?.Profiles ?? [];
            var colors = _parent?.BadgeColors ?? [];

            for (int idx = 0; idx < profiles.Count; idx++)
            {
                var card = BuildProfileCard(profiles[idx], colors);
                RootPanel.Children.Insert(RootPanel.Children.Count - 1, card);
            }
        }

        private CommunityToolkit.WinUI.Controls.SettingsExpander BuildProfileCard(
            ProfileConfig profile, Color[] colors)
        {
            var colorIdx = profile.BadgeColorIndex
                ?? (Math.Abs(profile.Id.GetHashCode()) % Math.Max(colors.Length, 1));
            if (colorIdx < 0 || colorIdx >= colors.Length) colorIdx = 0;

            var badgeColor = colors.Length > 0 ? colors[colorIdx] : Color.FromArgb(255, 128, 128, 128);

            var badgeIcon = new FontIcon
            {
                Glyph      = "",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Foreground = new SolidColorBrush(badgeColor),
            };

            var expander = new CommunityToolkit.WinUI.Controls.SettingsExpander
            {
                Header      = profile.Name,
                Description = profile.Id == "default"
                    ? R.Get("Profiles_DefaultDescription")
                    : profile.Id,
                HeaderIcon  = badgeIcon,
            };

            // ── 名前 ──
            var nameBox = new TextBox
            {
                Text     = profile.Name,
                MinWidth = 200,
            };
            nameBox.LostFocus += (_, _) =>
            {
                var newName = nameBox.Text.Trim();
                if (newName.Length == 0 || newName == profile.Name) return;
                profile.Name = newName;
                expander.Header = newName;
                _parent?.ProfilesModified?.Invoke();
            };
            var nameCard = new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header  = R.Get("Profiles_Name"),
                Content = nameBox,
            };

            // ── バッジ色 ──
            var colorCombo = new ComboBox
            {
                Width   = 80,
                Padding = new Thickness(4),
            };
            for (int i = 0; i < colors.Length; i++)
            {
                colorCombo.Items.Add(new Border
                {
                    Width        = 16,
                    Height       = 16,
                    CornerRadius = new CornerRadius(3),
                    Background   = new SolidColorBrush(colors[i]),
                });
            }
            colorCombo.SelectedIndex = colorIdx;
            colorCombo.SelectionChanged += (_, _) =>
            {
                profile.BadgeColorIndex = colorCombo.SelectedIndex;
                if (colors.Length > 0 && expander.HeaderIcon is FontIcon icon)
                    icon.Foreground = new SolidColorBrush(
                        colors[Math.Clamp(colorCombo.SelectedIndex, 0, colors.Length - 1)]);
                _parent?.ProfilesModified?.Invoke();
            };
            var colorCard = new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header  = R.Get("Profiles_BadgeColor"),
                Content = colorCombo,
            };

            // ── バッジテキスト ──
            var defaultBadgeText = profile.Name.Length > 3 ? profile.Name[..3] : profile.Name;
            var badgeTextBox = new TextBox
            {
                Text            = profile.BadgeText ?? defaultBadgeText,
                MaxLength       = 5,
                Width           = 120,
                PlaceholderText = defaultBadgeText,
            };
            badgeTextBox.LostFocus += (_, _) =>
            {
                var text = badgeTextBox.Text.Trim();
                var currentDefault = profile.Name.Length > 3 ? profile.Name[..3] : profile.Name;
                profile.BadgeText = text.Length > 0 && text != currentDefault ? text : null;
                _parent?.ProfilesModified?.Invoke();
            };
            var badgeTextCard = new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header  = R.Get("Profiles_BadgeText"),
                Content = badgeTextBox,
            };

            expander.Items.Add(nameCard);
            expander.Items.Add(colorCard);
            expander.Items.Add(badgeTextCard);

            // ── アクションボタン（初期化/削除）──
            if (profile.Id == "default")
            {
                // 初期化は重大な処理のため、フラグ方式で再起動まで実削除を遅延させ、
                // それまで取り消せるようにする (#86)。
                bool pending = _parent?.Settings.ResetDefaultProfilePending ?? false;
                if (pending)
                {
                    expander.Description = R.Get("Profile_ResetPendingInfo");
                    var cancelBtn = new Button { Content = R.Get("Profile_ResetCancel") };
                    cancelBtn.Click += (_, _) =>
                    {
                        if (_parent is null) return;
                        _parent.Settings.ResetDefaultProfilePending = false;
                        _parent.SaveSettingsOnly?.Invoke();
                        PopulateUI();
                    };
                    expander.Content = cancelBtn;
                }
                else
                {
                    var resetBtn = new Button { Content = R.Get("Profile_Reset") };
                    resetBtn.Click += async (_, _) =>
                    {
                        var dlg = new ContentDialog
                        {
                            Title             = R.Get("Profile_ResetConfirmTitle"),
                            Content           = R.Get("Profile_ResetConfirmBody"),
                            PrimaryButtonText = R.Get("Profile_ResetConfirm"),
                            CloseButtonText   = R.Get("Button_Cancel"),
                            DefaultButton     = ContentDialogButton.Close,
                            XamlRoot          = XamlRoot,
                            RequestedTheme    = ((FrameworkElement)_parent!.Content).ActualTheme,
                        };
                        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
                        {
                            if (_parent is null) return;
                            _parent.Settings.ResetDefaultProfilePending = true;
                            _parent.SaveSettingsOnly?.Invoke();
                            PopulateUI();
                        }
                    };
                    expander.Content = resetBtn;
                }
            }
            else
            {
                var deleteBtn = new Button { Content = R.Get("Profile_Delete") };
                var capturedProfile = profile;
                deleteBtn.Click += async (_, _) =>
                {
                    var count = _parent?.GetTimelineCount?.Invoke(capturedProfile.Id) ?? 0;
                    var dlg = new ContentDialog
                    {
                        Title             = R.Get("Profile_DeleteConfirmTitle"),
                        Content           = string.Format(R.Get("Profile_DeleteConfirmBody"), count),
                        PrimaryButtonText = R.Get("Profile_DeleteConfirm"),
                        CloseButtonText   = R.Get("Button_Cancel"),
                        DefaultButton     = ContentDialogButton.Close,
                        XamlRoot          = XamlRoot,
                        RequestedTheme    = ((FrameworkElement)_parent!.Content).ActualTheme,
                    };
                    if (await dlg.ShowAsync() == ContentDialogResult.Primary)
                    {
                        if (_parent?.DeleteProfileAsync is not null)
                            await _parent.DeleteProfileAsync(capturedProfile.Id);
                        PopulateUI();
                    }
                };
                expander.Content = deleteBtn;
            }

            return expander;
        }

        private void AddProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_parent is null) return;

            var win = new AddProfileWindow();
            var childTheme = ((FrameworkElement)_parent.Content).ActualTheme;
            ((FrameworkElement)win.Content).RequestedTheme = childTheme;
            MainWindow.ApplyTitleBarTheme(win, childTheme);

            // AddProfileWindow を SettingsWindow に対してモーダル化
            _parent.SetEnabled(false);
            win.Closed += (_, _) => _parent.SetEnabled(true);

            win.ProfileCreated += (_, profile) =>
            {
                _parent.Profiles.Add(profile);
                _parent.OnProfileCreated?.Invoke(profile);
                PopulateUI();
            };

            win.Activate();
        }
    }
}
