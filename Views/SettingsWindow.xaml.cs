using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace XTimelineViewer.Views
{
    public sealed partial class SettingsWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

        private readonly IntPtr _ownerHwnd;

        public SettingsWindow(IntPtr ownerHwnd)
        {
            _ownerHwnd = ownerHwnd;
            this.InitializeComponent();

            // ウィンドウサイズ・アイコン設定
            AppWindow.Resize(new SizeInt32(900, 620));
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);

            // ナビゲーション項目のテキストを設定
            RefreshNavText();

            // モーダル化: 親ウィンドウを無効化
            EnableWindow(_ownerHwnd, false);
            Closed += (_, _) => EnableWindow(_ownerHwnd, true);

            // 初期ページを選択
            NavView.SelectedItem = NavGeneral;
        }

        /// <summary>
        /// 親ウィンドウのテーマを設定ウィンドウにも適用する。
        /// </summary>
        public void ApplyTheme(ElementTheme theme)
        {
            ((FrameworkElement)Content).RequestedTheme = theme;
            MainWindow.ApplyTitleBarTheme(this, theme);
        }

        private void RefreshNavText()
        {
            Title                = R.Get("AppSettings_Title");
            NavGeneral.Content     = R.Get("Nav_General");
            NavExperimental.Content = R.Get("Nav_Experimental");
            NavExtensions.Content  = R.Get("Nav_Extensions");
            NavProfiles.Content    = R.Get("Nav_Profiles");
            NavAbout.Content       = R.Get("Nav_About");
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item) return;
            var tag = item.Tag?.ToString();

            var pageType = tag switch
            {
                "General"      => typeof(Settings.GeneralPage),
                "Experimental" => typeof(Settings.ExperimentalPage),
                "Extensions"   => typeof(Settings.ExtensionsPage),
                "Profiles"     => typeof(Settings.ProfilesPage),
                "About"        => typeof(Settings.AboutPage),
                _              => null,
            };

            if (pageType is not null)
                ContentFrame.Navigate(pageType);
        }
    }
}
