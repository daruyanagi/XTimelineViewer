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

            // 状態 InfoBar（#241）。拡張の有無にかかわらず常時表示し、フォルダーを開くボタンを添える。
            ExtensionsInfoBar.Message = extensions.Count == 0
                ? R.Get("Extensions_InfoBar_Empty")
                : R.Get("Extensions_InfoBar_Installed");
            OpenExtensionsFolderBtn.Content = R.Get("Extensions_OpenFolder");

            foreach (var ext in extensions)
            {
                AddExtensionCard(ext);
            }
        }

        // extensions フォルダーを開く（#241）。無ければ作成してから開く。
        private async void OpenExtensionsFolder_Click(object sender, RoutedEventArgs e)
        {
            var dir = MainWindow.GetExtensionsDir();
            try { Directory.CreateDirectory(dir); } catch { /* 作成失敗は無視して開くを試みる */ }
            await Windows.System.Launcher.LaunchFolderPathAsync(dir);
        }

        private void AddExtensionCard(ExtensionInfo ext)
        {
            var card = new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header      = ext.Name,
                Description = Path.GetFileName(ext.DirectoryPath),
                // 右端のリンクアイコンと「設定を開く」ボタンの機能が重複していたため、
                // 明示的なボタンを残してカード自体のクリック化は廃止
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
