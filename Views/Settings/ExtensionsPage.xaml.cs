using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using XTimelineViewer.Models;

namespace XTimelineViewer.Views.Settings
{
    public sealed partial class ExtensionsPage : Page
    {
        private SettingsWindow? _parent;

        public ExtensionsPage()
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
            PageTitle.Text = R.Get("Nav_Extensions");

            var extensions = _parent?.Extensions ?? [];

            if (extensions.Count == 0)
            {
                var emptyCard = new CommunityToolkit.WinUI.Controls.SettingsCard
                {
                    Header      = R.Get("Extensions_Empty"),
                    Description = R.Get("Extensions_Empty_Description"),
                    IsEnabled   = false,
                };
                emptyCard.HeaderIcon = new FontIcon
                {
                    Glyph      = "",
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons")
                };
                RootPanel.Children.Add(emptyCard);
                return;
            }

            foreach (var ext in extensions)
            {
                AddExtensionCard(ext);
            }
        }

        private void AddExtensionCard(ExtensionInfo ext)
        {
            var card = new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header      = ext.Name,
                Description = Path.GetFileName(ext.DirectoryPath),
                IsClickEnabled = ext.OptionsPage is not null && ext.ExtensionId is not null,
            };

            if (ext.IconPath is not null)
            {
                card.HeaderIcon = new ImageIcon
                {
                    Source = new BitmapImage(new Uri(ext.IconPath)),
                    Width  = 24,
                    Height = 24,
                };
            }
            else
            {
                card.HeaderIcon = new FontIcon
                {
                    Glyph      = "",
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons")
                };
            }

            if (card.IsClickEnabled)
            {
                card.ActionIcon = new FontIcon
                {
                    Glyph      = "",
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
                    FontSize   = 14,
                };
                card.Click += async (_, _) =>
                {
                    if (_parent?.OpenExtensionSettingsAsync is not null && XamlRoot is not null)
                        await _parent.OpenExtensionSettingsAsync(ext, XamlRoot);
                };
            }

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing     = 8,
            };

            if (ext.OptionsPage is not null && ext.ExtensionId is not null)
            {
                var settingsBtn = new Button
                {
                    Content = R.Get("ExtSettings_OpenSettings"),
                };
                settingsBtn.Click += async (_, _) =>
                {
                    if (_parent?.OpenExtensionSettingsAsync is not null && XamlRoot is not null)
                        await _parent.OpenExtensionSettingsAsync(ext, XamlRoot);
                };
                buttonsPanel.Children.Add(settingsBtn);
            }

            // 拡張機能カードのリンクボタン（Chrome Web Store 等）は場所をとるため削除

            if (buttonsPanel.Children.Count > 0)
                card.Content = buttonsPanel;

            RootPanel.Children.Add(card);
        }
    }
}
