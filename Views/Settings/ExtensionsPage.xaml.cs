using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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

            AddInstallCard();

            foreach (var ext in extensions)
            {
                AddExtensionCard(ext);
            }
        }

        /// <summary>
        /// GitHub のリリースから入れる（#399）。
        ///
        /// Chrome Web ストアは扱わない。WebView2 にストアからインストールする API が無く、
        /// .crx の配信エンドポイントは非公開で、利用規約もプログラムからの取得を制限している。
        /// GitHub はリリース API が公開されているので正攻法で実装できる。
        /// </summary>
        private void AddInstallCard()
        {
            var urlBox = new TextBox
            {
                PlaceholderText = "https://github.com/owner/repo",
                MinWidth        = 280,
            };
            AutomationProperties.SetName(urlBox, R.Get("Extensions_InstallFromGitHub"));
            AutomationProperties.SetAutomationId(urlBox, "ExtInstallUrl");

            var installBtn = new Button { Content = R.Get("Extensions_Install") };
            AutomationProperties.SetAutomationId(installBtn, "ExtInstallBtn");
            installBtn.Click += async (_, _) => await InstallFromGitHubAsync(urlBox.Text, installBtn);

            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            panel.Children.Add(urlBox);
            panel.Children.Add(installBtn);

            RootPanel.Children.Add(new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header      = R.Get("Extensions_InstallFromGitHub"),
                Description = R.Get("Extensions_InstallFromGitHub_Desc"),
                HeaderIcon  = new FontIcon
                {
                    Glyph      = "\uE896",
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
                },
                Content = panel,
            });
        }

        private async Task InstallFromGitHubAsync(string url, Button trigger)
        {
            if (_parent?.FindExtensionCandidatesAsync is null ||
                _parent.PrepareExtensionAsync is null ||
                _parent.CommitExtensionAsync is null ||
                XamlRoot is null) return;

            trigger.IsEnabled = false;
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(5));

                var (status, candidates) = await _parent.FindExtensionCandidatesAsync(url, cts.Token);
                if (status != Services.ExtensionInstallRunner.Status.Ok)
                {
                    await ShowInstallMessageAsync(MessageFor(status));
                    return;
                }

                // 複数あるときは選ばせる。どれが本体かは相手の付け方次第で決められない。
                var candidate = candidates.Count == 1
                    ? candidates[0]
                    : await PickCandidateAsync(candidates);
                if (candidate is null) return;

                var prepared = await _parent.PrepareExtensionAsync(candidate, null, cts.Token);
                if (prepared.Status != Services.ExtensionInstallRunner.Status.Ok)
                {
                    await ShowInstallMessageAsync(MessageFor(prepared.Status));
                    return;
                }

                if (!await ConfirmInstallAsync(prepared))
                {
                    // 取りやめたら一時ファイルを残さない
                    Services.ExtensionInstallRunner.Discard(prepared);
                    return;
                }

                if (await _parent.CommitExtensionAsync(prepared))
                {
                    await ShowInstallMessageAsync(string.Format(R.Get("Extensions_Install_Done"), prepared.Name));
                    RootPanel.Children.Clear();
                    PopulateUI();
                }
                else
                {
                    await ShowInstallMessageAsync(MessageFor(Services.ExtensionInstallRunner.Status.Failed));
                }
            }
            finally
            {
                trigger.IsEnabled = true;
            }
        }

        private static string MessageFor(Services.ExtensionInstallRunner.Status status) => status switch
        {
            Services.ExtensionInstallRunner.Status.BadUrl           => R.Get("Extensions_Install_BadUrl"),
            Services.ExtensionInstallRunner.Status.NoRelease        => R.Get("Extensions_Install_NoRelease"),
            Services.ExtensionInstallRunner.Status.NoAsset          => R.Get("Extensions_Install_NoAsset"),
            Services.ExtensionInstallRunner.Status.NotAnExtension   => R.Get("Extensions_Install_NotAnExtension"),
            Services.ExtensionInstallRunner.Status.AlreadyInstalled => R.Get("Extensions_Install_Already"),
            Services.ExtensionInstallRunner.Status.Canceled         => R.Get("Extensions_Install_Canceled"),
            _                                                      => R.Get("Extensions_Install_Failed"),
        };

        private async Task<Services.ExtensionInstaller.Candidate?> PickCandidateAsync(
            IReadOnlyList<Services.ExtensionInstaller.Candidate> candidates)
        {
            var list = new ListBox { SelectionMode = SelectionMode.Single };
            foreach (var c in candidates) list.Items.Add(c.Name);
            list.SelectedIndex = 0;

            var dlg = new ContentDialog
            {
                Title             = R.Get("Extensions_Install_PickTitle"),
                Content           = list,
                PrimaryButtonText = R.Get("Extensions_Install"),
                CloseButtonText   = R.Get("Button_Cancel"),
                XamlRoot          = XamlRoot,
            };

            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return null;
            return list.SelectedIndex >= 0 ? candidates[list.SelectedIndex] : null;
        }

        /// <summary>
        /// 何を許すことになるのかを見せてから入れる（#399）。
        ///
        /// 拡張機能は X のページ上で任意のコードを実行でき、Cookie や DOM に触れる。
        /// 取得元と manifest.json の権限を並べて、利用者に判断してもらう。
        /// </summary>
        private async Task<bool> ConfirmInstallAsync(Services.ExtensionInstallRunner.Prepared prepared)
        {
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(new TextBlock
            {
                Text         = $"{prepared.Name}  {prepared.Version}",
                FontWeight   = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            });
            body.Children.Add(new TextBlock
            {
                Text         = string.Format(R.Get("Extensions_Install_From"), prepared.SourceUrl),
                FontSize     = 12,
                Opacity      = 0.8,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            });

            body.Children.Add(new TextBlock
            {
                Text         = prepared.Permissions.Count == 0
                                 ? R.Get("Extensions_Install_NoPermissions")
                                 : R.Get("Extensions_Install_Permissions"),
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 8, 0, 0),
            });

            if (prepared.Permissions.Count > 0)
            {
                body.Children.Add(new ScrollViewer
                {
                    MaxHeight = 160,
                    Content   = new TextBlock
                    {
                        Text         = string.Join(Environment.NewLine, prepared.Permissions),
                        FontFamily   = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
                        FontSize     = 12,
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true,
                    },
                });
            }

            body.Children.Add(new TextBlock
            {
                Text         = R.Get("Extensions_Install_Warning"),
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 8, 0, 0),
            });

            var dlg = new ContentDialog
            {
                Title             = R.Get("Extensions_Install_ConfirmTitle"),
                Content           = new ScrollViewer { MaxHeight = 380, Content = body },
                PrimaryButtonText = R.Get("Extensions_Install"),
                CloseButtonText   = R.Get("Button_Cancel"),
                XamlRoot          = XamlRoot,
            };

            return await dlg.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task ShowInstallMessageAsync(string message)
        {
            var dlg = new ContentDialog
            {
                Title           = R.Get("Extensions_InstallFromGitHub"),
                Content         = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = R.Get("Button_Close"),
                XamlRoot        = XamlRoot,
            };
            await dlg.ShowAsync();
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
            // プロファイル別の有効・無効とアンインストールを畳んで持たせる（#398）。
            // 拡張機能が増えても縦に伸びすぎないよう、既定は閉じておく。
            var card = new CommunityToolkit.WinUI.Controls.SettingsExpander
            {
                Header      = ext.Name,
                Description = Path.GetFileName(ext.DirectoryPath),
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

            AddToggleRows(card, ext);

            RootPanel.Children.Add(card);
        }

        /// <summary>
        /// プロファイル別の有効・無効、新規プロファイルでの既定、アンインストール（#398）。
        /// </summary>
        private void AddToggleRows(CommunityToolkit.WinUI.Controls.SettingsExpander card, ExtensionInfo ext)
        {
            if (_parent is null) return;

            var key = Path.GetFileName(ext.DirectoryPath);

            foreach (var profile in _parent.Profiles)
            {
                var toggle = new ToggleSwitch
                {
                    IsOn = _parent.IsExtensionEnabled?.Invoke(key, profile.Id) ?? true,
                };
                // Header を持たないので名前を与える（#344）
                AutomationProperties.SetName(toggle, $"{ext.Name} / {profile.Name}");
                AutomationProperties.SetAutomationId(toggle, $"ExtToggle_{key}_{profile.Id}");

                toggle.Toggled += async (sender, _) =>
                {
                    if (_parent.SetExtensionEnabledAsync is null) return;
                    await _parent.SetExtensionEnabledAsync(key, profile.Id, ((ToggleSwitch)sender).IsOn);
                };

                card.Items.Add(new CommunityToolkit.WinUI.Controls.SettingsCard
                {
                    Header  = profile.Name,
                    Content = toggle,
                });
            }

            // 新しく追加されたプロファイルでの既定。
            // 既定を有効にしてあるので、触らなければ「入れたものはどこでも効く」まま。
            var defaultToggle = new ToggleSwitch
            {
                IsOn = _parent.IsExtensionEnabledByDefault?.Invoke(key) ?? true,
            };
            AutomationProperties.SetName(defaultToggle, $"{ext.Name} / {R.Get("Extensions_DefaultForNewProfiles")}");
            AutomationProperties.SetAutomationId(defaultToggle, $"ExtDefault_{key}");
            defaultToggle.Toggled += (sender, _) =>
                _parent.SetExtensionDefault?.Invoke(key, ((ToggleSwitch)sender).IsOn);

            card.Items.Add(new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header      = R.Get("Extensions_DefaultForNewProfiles"),
                Description = R.Get("Extensions_DefaultForNewProfiles_Desc"),
                Content     = defaultToggle,
            });

            // アンインストール
            var uninstallBtn = new Button { Content = R.Get("Extensions_Uninstall") };
            AutomationProperties.SetAutomationId(uninstallBtn, $"ExtUninstall_{key}");
            uninstallBtn.Click += async (_, _) => await UninstallAsync(key, ext.Name, card);

            card.Items.Add(new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header      = R.Get("Extensions_Uninstall"),
                Description = R.Get("Extensions_Uninstall_Desc"),
                Content     = uninstallBtn,
            });
        }

        private async Task UninstallAsync(string key, string name, UIElement card)
        {
            if (_parent?.UninstallExtensionAsync is null || XamlRoot is null) return;

            var confirm = new ContentDialog
            {
                Title             = R.Get("Extensions_Uninstall"),
                Content           = new TextBlock
                {
                    Text         = string.Format(R.Get("Extensions_Uninstall_Confirm"), name),
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = R.Get("Extensions_Uninstall"),
                CloseButtonText   = R.Get("Button_Cancel"),
                XamlRoot          = XamlRoot,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            if (await _parent.UninstallExtensionAsync(key))
            {
                RootPanel.Children.Remove(card);
                return;
            }

            // 消せなかったことを黙って飲み込まない。中途半端な状態のまま
            // 「消えた」と思われるのが困る。
            var failed = new ContentDialog
            {
                Title             = R.Get("Extensions_Uninstall"),
                Content           = new TextBlock
                {
                    Text         = R.Get("Extensions_Uninstall_Failed"),
                    TextWrapping = TextWrapping.Wrap,
                },
                CloseButtonText   = R.Get("Button_Close"),
                XamlRoot          = XamlRoot,
            };
            await failed.ShowAsync();
        }
    }
}
