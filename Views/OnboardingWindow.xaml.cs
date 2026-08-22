using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using Windows.Graphics;
using XTimelineViewer.Models;
using XTimelineViewer.Services;

namespace XTimelineViewer.Views
{
    /// <summary>
    /// 初回起動時のオンボーディングウィザード (#4)。
    /// Step1: ようこそ → Step2: X ログイン (ProfileLoginControl) → Step3: 完了
    /// ShowModal パターンで MainWindow に対してモーダル表示する。
    /// </summary>
    public sealed partial class OnboardingWindow : Window
    {
        private enum Step { Welcome, Login, Complete }
        private Step _currentStep = Step.Welcome;
        private AppSettings? _appSettings;
        private event EventHandler<ProfileConfig>? ProfileCreated;

        private OnboardingWindow()
        {
            this.InitializeComponent();
            AppWindow.Resize(new SizeInt32(520, 680));

            // Step 1 テキスト
            WelcomeTitle.Text = R.Get("Onboarding_WelcomeTitle");
            WelcomeBody.Text  = R.Get("Onboarding_WelcomeBody");
            SkipBtn.Content   = R.Get("Button_Skip");
            PrimaryBtn.Content = R.Get("Onboarding_StartBtn");

            // Step 2: ログイン検出で作成ボタンを有効化
            LoginControl.LoginDetected += _ =>
            {
                PrimaryBtn.Content   = R.Get("AddProfile_CreateBtn");
                PrimaryBtn.IsEnabled = true;
            };
        }

        /// <summary>
        /// owner に対してモーダルでオンボーディングを開く。
        /// onCreated: プロファイル作成時（ウィンドウはまだ開いている）。プロファイル保存等に使う。
        /// onClosed: ウィンドウが閉じた後。WebView2 env が解放されるため、タイムライン追加はここで行う。
        /// </summary>
        public static void ShowModal(Window owner, AppSettings appSettings, Action<ProfileConfig> onCreated, Action? onClosed = null)
        {
            var win = new OnboardingWindow();
            win._appSettings = appSettings;
            var theme = ((FrameworkElement)owner.Content).ActualTheme;
            ((FrameworkElement)win.Content).RequestedTheme = theme;
            MainWindow.ApplyTitleBarTheme(win, theme);
            win.ProfileCreated += (_, profile) => onCreated(profile);
            win.Closed += (_, _) => onClosed?.Invoke();
            WindowModal.RunModal(owner, win);
        }

        private void PrimaryBtn_Click(object sender, RoutedEventArgs e)
        {
            switch (_currentStep)
            {
                case Step.Welcome:
                    GoToLogin();
                    break;
                case Step.Login:
                    TryCreateProfile();
                    break;
                case Step.Complete:
                    Close();
                    break;
            }
        }

        private void SkipBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
            Microsoft.UI.Xaml.Application.Current.Exit();
        }

        private void GoToLogin()
        {
            _currentStep = Step.Login;

            WelcomeStep.Visibility   = Visibility.Collapsed;
            LoginControl.Visibility  = Visibility.Visible;

            Title = R.Get("Onboarding_LoginTitle");
            PrimaryBtn.Content   = R.Get("Onboarding_DoneBtn");
            PrimaryBtn.IsEnabled = false; // ログイン検出まで無効
            SkipBtn.Content      = R.Get("Button_Skip");

            LoginControl.InitializeAsync().FireAndForget("LoginControl.InitializeAsync");
        }

        private void TryCreateProfile()
        {
            var profile = LoginControl.BuildProfile();
            if (profile is null) return;

            Debug.WriteLine($"[Onboarding] Profile created: Id={profile.Id}, Name={profile.Name}");
            ProfileCreated?.Invoke(this, profile);

            GoToComplete(profile.Name);
        }

        private void GoToComplete(string profileName)
        {
            _currentStep = Step.Complete;

            // WebView2 env を解放し、ウィンドウを閉じた後に MainWindow が
            // 同じプロファイルの env を新規作成できるようにする
            LoginControl.CloseWebView();
            LoginControl.Visibility  = Visibility.Collapsed;
            CompleteStep.Visibility  = Visibility.Visible;

            Title = R.Get("Onboarding_CompleteTitle");
            CompleteTitle.Text = R.Get("Onboarding_CompleteTitle");
            CompleteBody.Text  = string.Format(R.Get("Onboarding_CompleteBody"), profileName);

            // タイムラインの既定表示オプション
            if (_appSettings is not null)
            {
                var s = _appSettings;

                SidebarLabel.Text          = R.Get("Settings_DefaultSidebar");
                SidebarDesc.Text           = R.Get("Settings_DefaultSidebar_Description");
                SidebarToggle.OnContent    = R.Get("Toggle_Show");
                SidebarToggle.OffContent   = R.Get("Toggle_Hide");
                SidebarToggle.IsOn         = !s.DefaultHideSidebar;

                ComposeLabel.Text          = R.Get("Settings_DefaultCompose");
                ComposeDesc.Text           = R.Get("Settings_DefaultCompose_Description");
                ComposeToggle.OnContent    = R.Get("Toggle_Show");
                ComposeToggle.OffContent   = R.Get("Toggle_Hide");
                ComposeToggle.IsOn         = !s.DefaultHideCompose;

                ListHeaderLabel.Text       = R.Get("Settings_DefaultListHeader");
                ListHeaderDesc.Text        = R.Get("Settings_DefaultListHeader_Description");
                ListHeaderToggle.OnContent = R.Get("Toggle_Show");
                ListHeaderToggle.OffContent= R.Get("Toggle_Hide");
                ListHeaderToggle.IsOn      = !s.DefaultHideListHeader;

                DefaultsHintText.Text       = R.Get("Onboarding_DefaultsHint");
                DefaultsHintText.Visibility = Visibility.Visible;

                DefaultsPanel.Visibility = Visibility.Visible;
            }

            PrimaryBtn.Content   = R.Get("Button_Close");
            PrimaryBtn.IsEnabled = true;
            SkipBtn.Visibility   = Visibility.Collapsed;
        }

        private void SidebarToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_appSettings is null) return;
            _appSettings.DefaultHideSidebar = !SidebarToggle.IsOn;
        }

        private void ComposeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_appSettings is null) return;
            _appSettings.DefaultHideCompose = !ComposeToggle.IsOn;
        }

        private void ListHeaderToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_appSettings is null) return;
            _appSettings.DefaultHideListHeader = !ListHeaderToggle.IsOn;
        }
    }
}
