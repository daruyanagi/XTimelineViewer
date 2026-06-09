using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using XTimelineViewer.Models;

namespace XTimelineViewer.Views.Controls
{
    /// <summary>
    /// タイムラインの既定表示オプション（サイドバー/コンポーズ/リストヘッダー）を
    /// 設定する再利用可能なコントロール (#27)。
    /// GeneralPage（設定画面）と OnboardingWindow（初回起動）の両方で使用する。
    /// </summary>
    public sealed partial class TimelineDefaultsControl : UserControl
    {
        private AppSettings? _settings;
        private bool _isInitializing;

        /// <summary>設定値が変更されたときに発火する。ホスト側で保存処理などに使う。</summary>
        public event Action? Changed;

        public TimelineDefaultsControl()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// AppSettings を受け取り、トグルの初期状態を設定する。
        /// </summary>
        /// <param name="settings">読み書き対象の AppSettings。</param>
        /// <param name="showHint">ヒントテキスト（「後から変更できます」）を表示するか。</param>
        public void Initialize(AppSettings settings, bool showHint = false)
        {
            _isInitializing = true;
            _settings = settings;

            // ラベル設定
            SidebarToggle.Header    = R.Get("Settings_DefaultSidebar");
            SidebarToggle.OnContent  = R.Get("Toggle_Show");
            SidebarToggle.OffContent = R.Get("Toggle_Hide");

            ComposeToggle.Header    = R.Get("Settings_DefaultCompose");
            ComposeToggle.OnContent  = R.Get("Toggle_Show");
            ComposeToggle.OffContent = R.Get("Toggle_Hide");

            ListHeaderToggle.Header    = R.Get("Settings_DefaultListHeader");
            ListHeaderToggle.OnContent  = R.Get("Toggle_Show");
            ListHeaderToggle.OffContent = R.Get("Toggle_Hide");

            // 反転ロジック: IsOn=true → 表示 → Hide=false
            SidebarToggle.IsOn    = !settings.DefaultHideSidebar;
            ComposeToggle.IsOn    = !settings.DefaultHideCompose;
            ListHeaderToggle.IsOn = !settings.DefaultHideListHeader;

            if (showHint)
            {
                HintText.Text = R.Get("Onboarding_DefaultsHint");
                HintText.Visibility = Visibility.Visible;
            }

            _isInitializing = false;
        }

        private void SidebarToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _settings is null) return;
            _settings.DefaultHideSidebar = !SidebarToggle.IsOn;
            Changed?.Invoke();
        }

        private void ComposeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _settings is null) return;
            _settings.DefaultHideCompose = !ComposeToggle.IsOn;
            Changed?.Invoke();
        }

        private void ListHeaderToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _settings is null) return;
            _settings.DefaultHideListHeader = !ListHeaderToggle.IsOn;
            Changed?.Invoke();
        }
    }
}
