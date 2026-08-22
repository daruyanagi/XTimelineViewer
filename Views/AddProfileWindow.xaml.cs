using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using Windows.Graphics;
using XTimelineViewer.Models;
using XTimelineViewer.Services;

namespace XTimelineViewer.Views
{
    public sealed partial class AddProfileWindow : Window
    {
        // 生成は ShowModal 経由のみ。呼び出し元に関わらずモーダルを型で保証する (#157)。
        private event EventHandler<ProfileConfig>? ProfileCreated;

        private AddProfileWindow()
        {
            this.InitializeComponent();
            AppWindow.Resize(new SizeInt32(500, 700));
            Title = R.Get("AddProfile_Title");
            CancelBtn.Content = R.Get("Button_Cancel");
            CreateBtn.Content = R.Get("AddProfile_CreateBtn");

            // ログイン検出で作成ボタンを表示する。実処理は共通コントロールが担う (#157)。
            LoginControl.LoginDetected += _ => CreateBtn.Visibility = Visibility.Visible;
            LoginControl.InitializeAsync().FireAndForget("LoginControl.InitializeAsync");
        }

        /// <summary>
        /// owner に対してモーダルでプロファイル作成ウィンドウを開く (#157)。
        /// テーマ・タイトルバー・モーダル化・コールバック配線をすべてここに集約し、
        /// 呼び出し元（設定画面・メニュー・将来のオンボーディング）はこの 1 行で済む。
        /// </summary>
        public static void ShowModal(Window owner, Action<ProfileConfig> onCreated)
        {
            var win = new AddProfileWindow();
            var theme = ((FrameworkElement)owner.Content).ActualTheme;
            ((FrameworkElement)win.Content).RequestedTheme = theme;
            MainWindow.ApplyTitleBarTheme(win, theme);
            win.ProfileCreated += (_, profile) => onCreated(profile);
            WindowModal.RunModal(owner, win);
        }

        private void CreateBtn_Click(object sender, RoutedEventArgs e)
        {
            var profile = LoginControl.BuildProfile();
            if (profile is null) return;
            Debug.WriteLine($"[Profile] Created: Id={profile.Id}, Name={profile.Name}");
            ProfileCreated?.Invoke(this, profile);
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine($"[Profile] Cancelled: Id={LoginControl.ProfileId} (will be cleaned up on next launch)");
            Close();
        }
    }
}
