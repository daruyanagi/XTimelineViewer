using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using XTimelineViewer.Services;

namespace XTimelineViewer.Views.Settings
{
    public sealed partial class ExperimentalPage : Page
    {
        private static readonly string[] BrowserValues = ["system", "edge"];

        private SettingsWindow? _parent;
        private bool _isInitializing = true;
        private List<EdgeProfile> _edgeProfiles = [];

        public ExperimentalPage()
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
            _isInitializing = true;
            var s = _parent?.Settings;

            PageTitle.Text = R.Get("Nav_Experimental");

            // Open Composer in Browser
            OpenComposerCard.Header      = R.Get("Settings_OpenComposerInBrowser");
            OpenComposerCard.Description = R.Get("Settings_OpenComposerInBrowser_Description");
            OpenComposerToggle.OnContent  = R.Get("Toggle_On");
            OpenComposerToggle.OffContent = R.Get("Toggle_Off");
            OpenComposerToggle.IsOn       = s?.OpenComposerInBrowser ?? false;

            // Open Timestamp in Browser
            OpenTimestampCard.Header      = R.Get("Settings_OpenTimestampInBrowser");
            OpenTimestampCard.Description = R.Get("Settings_OpenTimestampInBrowser_Description");
            OpenTimestampToggle.OnContent  = R.Get("Toggle_On");
            OpenTimestampToggle.OffContent = R.Get("Toggle_Off");
            OpenTimestampToggle.IsOn       = s?.OpenTimestampInBrowser ?? false;

            // Auto-Activate
            AutoActivateCard.Header      = R.Get("Settings_AutoActivate");
            AutoActivateCard.Description = R.Get("Settings_AutoActivate_Description");
            AutoActivateBox.Value        = s?.AutoActivateMinutes ?? 0;

            // Show Auto-Activate Label
            ShowAutoActivateLabelCard.Header      = R.Get("Settings_ShowAutoActivateLabel");
            ShowAutoActivateLabelCard.Description = R.Get("Settings_ShowAutoActivateLabel_Description");
            ShowAutoActivateLabelToggle.OnContent  = R.Get("Toggle_On");
            ShowAutoActivateLabelToggle.OffContent = R.Get("Toggle_Off");
            ShowAutoActivateLabelToggle.IsOn       = s?.ShowAutoActivateLabel ?? false;

            // External Browser Section
            ExternalBrowserHeader.Text = R.Get("Section_ExternalBrowser");

            BrowserCard.Header      = R.Get("Settings_ExternalBrowser");
            BrowserCard.Description = R.Get("Settings_ExternalBrowser_Description");
            BrowserCombo.ItemsSource = new List<string> { R.Get("Browser_System"), "Microsoft Edge" };
            BrowserCombo.SelectedIndex = s?.ExternalBrowser == "edge" ? 1 : 0;

            // Edge Profile
            EdgeProfileCard.Header      = R.Get("Settings_EdgeProfile");
            EdgeProfileCard.Description = R.Get("Settings_EdgeProfile_Description");
            _edgeProfiles = EdgeService.EnumerateProfiles();

            if (_edgeProfiles.Count == 0)
            {
                EdgeProfileCombo.ItemsSource   = new List<string> { R.Get("Browser_EdgeNotFound") };
                EdgeProfileCombo.SelectedIndex = 0;
                EdgeProfileCombo.IsEnabled     = false;
            }
            else
            {
                var names = new List<string>();
                int selectedIdx = 0;
                for (int i = 0; i < _edgeProfiles.Count; i++)
                {
                    var p = _edgeProfiles[i];
                    var detail = p.UserName.Length > 0 ? p.UserName : p.Directory;
                    names.Add($"{p.DisplayName}  ({detail})");
                    if (p.Directory == s?.EdgeProfileDirectory)
                        selectedIdx = i;
                }
                EdgeProfileCombo.ItemsSource   = names;
                EdgeProfileCombo.SelectedIndex = selectedIdx;
                EdgeProfileCombo.IsEnabled     = s?.ExternalBrowser == "edge";
            }

            _isInitializing = false;
        }

        private void OpenComposerToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _parent is null) return;
            _parent.Settings.OpenComposerInBrowser = OpenComposerToggle.IsOn;
            _parent.NotifySettingsChanged();
        }

        private void OpenTimestampToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _parent is null) return;
            _parent.Settings.OpenTimestampInBrowser = OpenTimestampToggle.IsOn;
            _parent.NotifySettingsChanged();
        }

        private void AutoActivateBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_isInitializing || _parent is null) return;
            _parent.Settings.AutoActivateMinutes = (int)Math.Clamp(args.NewValue, 0, 60);
            _parent.NotifySettingsChanged();
        }

        private void ShowAutoActivateLabelToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _parent is null) return;
            _parent.Settings.ShowAutoActivateLabel = ShowAutoActivateLabelToggle.IsOn;
            _parent.NotifySettingsChanged();
        }

        private void BrowserCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _parent is null) return;

            var idx = Math.Clamp(BrowserCombo.SelectedIndex, 0, BrowserValues.Length - 1);
            _parent.Settings.ExternalBrowser = BrowserValues[idx];

            // Edge プロファイル ComboBox の有効/無効を切り替え
            var isEdge = idx == 1;
            EdgeProfileCombo.IsEnabled = isEdge && _edgeProfiles.Count > 0;

            _parent.NotifySettingsChanged();
        }

        private void EdgeProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _parent is null) return;

            var idx = EdgeProfileCombo.SelectedIndex;
            if (_edgeProfiles.Count > 0 && idx >= 0 && idx < _edgeProfiles.Count)
                _parent.Settings.EdgeProfileDirectory = _edgeProfiles[idx].Directory;

            _parent.NotifySettingsChanged();
        }
    }
}
