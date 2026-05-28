using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI;

namespace XTimelineViewer
{
    public sealed partial class ManageProfilesWindow : Window
    {
        private readonly List<ProfileConfig> _profiles;
        private readonly Color[] _badgeColors;
        private readonly Func<string, int> _getTimelineCount;
        private readonly List<(ProfileConfig profile, TextBox nameBox, ComboBox colorBox, TextBox badgeTextBox, Grid row)> _rows = [];

        public event EventHandler<ProfileChangedEventArgs>? ProfilesChanged;
        public event EventHandler<string>? ProfileDeleteRequested;
        public event EventHandler<ProfileConfig>? ProfileCreated;

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        private const int GWLP_HWNDPARENT = -8;

        public ManageProfilesWindow(List<ProfileConfig> profiles, Color[] badgeColors, IntPtr ownerHwnd, Func<string, int> getTimelineCount)
        {
            this.InitializeComponent();
            _profiles = profiles;
            _badgeColors = badgeColors;
            _getTimelineCount = getTimelineCount;
            AppWindow.Resize(new SizeInt32(520, 400));
            Title = R.Get("Menu_ManageProfiles");
            CancelBtn.Content = R.Get("Button_Cancel");
            SaveBtn.Content = R.Get("Button_Save");
            AddProfileLabel.Text = R.Get("Menu_NewProfile");
            AutomationProperties.SetName(AddProfileBtn, R.Get("Menu_NewProfile"));

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, ownerHwnd);
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsModal = true;
                presenter.IsResizable = false;
            }

            BuildList();
        }

        private void BuildList()
        {
            ProfileList.Children.Clear();
            _rows.Clear();

            foreach (var profile in _profiles)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var colorIdx = profile.BadgeColorIndex
                    ?? (Math.Abs(profile.Id.GetHashCode()) % _badgeColors.Length);
                var colorBox = new ComboBox
                {
                    Width             = 50,
                    Margin            = new Thickness(0, 0, 6, 0),
                    Padding           = new Thickness(4),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                for (int i = 0; i < _badgeColors.Length; i++)
                {
                    colorBox.Items.Add(new Border
                    {
                        Width        = 16,
                        Height       = 16,
                        CornerRadius = new CornerRadius(3),
                        Background   = new SolidColorBrush(_badgeColors[i]),
                    });
                }
                colorBox.SelectedIndex = colorIdx;
                Grid.SetColumn(colorBox, 0);

                var nameBox = new TextBox
                {
                    Text              = profile.Name,
                    MinWidth          = 100,
                    Margin            = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(nameBox, 1);

                var name = profile.Name;
                var defaultBadgeText = name.Length > 3 ? name[..3] : name;
                var badgeTextBox = new TextBox
                {
                    Text            = profile.BadgeText ?? defaultBadgeText,
                    MaxLength       = 5,
                    Width           = 60,
                    Margin          = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    PlaceholderText = defaultBadgeText,
                };
                Grid.SetColumn(badgeTextBox, 2);

                var actionBtn = new Button
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth          = 60,
                };

                if (profile.Id == "default")
                {
                    actionBtn.Content = R.Get("Profile_Reset");
                    actionBtn.IsEnabled = false;
                }
                else
                {
                    actionBtn.Content = R.Get("Profile_Delete");
                    var capturedProfile = profile;
                    var capturedRow = row;
                    actionBtn.Click += async (s, e) =>
                    {
                        var dlg = new ContentDialog
                        {
                            Title             = R.Get("Profile_DeleteConfirmTitle"),
                            Content           = string.Format(R.Get("Profile_DeleteConfirmBody"), _getTimelineCount(capturedProfile.Id)),
                            PrimaryButtonText = R.Get("Profile_DeleteConfirm"),
                            CloseButtonText   = R.Get("Button_Cancel"),
                            DefaultButton     = ContentDialogButton.Close,
                            XamlRoot          = Content.XamlRoot,
                            RequestedTheme    = ((FrameworkElement)Content).ActualTheme,
                        };
                        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
                        {
                            ProfileDeleteRequested?.Invoke(this, capturedProfile.Id);
                            capturedRow.Visibility = Visibility.Collapsed;
                            Debug.WriteLine($"[Profile] Delete requested: {capturedProfile.Id}");
                        }
                    };
                }
                Grid.SetColumn(actionBtn, 3);

                row.Children.Add(colorBox);
                row.Children.Add(nameBox);
                row.Children.Add(badgeTextBox);
                row.Children.Add(actionBtn);
                ProfileList.Children.Add(row);
                _rows.Add((profile, nameBox, colorBox, badgeTextBox, row));
            }
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            var changes = new List<ProfileChange>();
            foreach (var (profile, nameBox, colorBox, badgeTextBox, row) in _rows)
            {
                if (row.Visibility == Visibility.Collapsed) continue;

                var newName = nameBox.Text.Trim();
                if (newName.Length == 0) newName = profile.Name;

                var newColorIdx = colorBox.SelectedIndex;
                var defaultColorIdx = profile.BadgeColorIndex
                    ?? (Math.Abs(profile.Id.GetHashCode()) % _badgeColors.Length);

                var newBadgeText = badgeTextBox.Text.Trim();
                var defaultText = newName.Length > 3 ? newName[..3] : newName;

                bool changed = newName != profile.Name
                    || newColorIdx != defaultColorIdx
                    || newBadgeText != (profile.BadgeText ?? defaultText);

                if (changed)
                {
                    changes.Add(new ProfileChange
                    {
                        ProfileId     = profile.Id,
                        NewName       = newName,
                        NewColorIndex = newColorIdx,
                        NewBadgeText  = newBadgeText.Length > 0 ? newBadgeText : null,
                    });
                }
            }

            if (changes.Count > 0)
                ProfilesChanged?.Invoke(this, new ProfileChangedEventArgs { Changes = changes });

            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void AddProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = new AddProfileWindow();
            win.ProfileCreated += (__, profile) =>
            {
                _profiles.Add(profile);
                ProfileCreated?.Invoke(this, profile);
                BuildList();
            };
            win.Activate();
        }
    }

    public class ProfileChange
    {
        public string  ProfileId     { get; set; } = "";
        public string  NewName       { get; set; } = "";
        public int     NewColorIndex { get; set; }
        public string? NewBadgeText  { get; set; }
    }

    public class ProfileChangedEventArgs : EventArgs
    {
        public List<ProfileChange> Changes { get; set; } = [];
    }
}
