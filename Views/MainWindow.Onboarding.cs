using Microsoft.UI.Xaml;
using System.Linq;
using System.Threading.Tasks;

using XTimelineViewer.Models;

using XTimelineViewer.Services;

namespace XTimelineViewer.Views
{
    public sealed partial class MainWindow : Window
    {
        /// <summary>
        /// 起動時の初期化フロー。タイムライン復元後、名前付きプロファイルがなければ
        /// オンボーディングをモーダル表示する (#4)。
        /// </summary>
        private async Task InitializeAsync()
        {
            RestoreTimelines();
            UpdateHasNamedProfiles();

            // 投稿ウィンドウのプリロード（#244 案B）。設定 OFF のときは内部で何もしない。
            WarmUpComposeAsync().FireAndForget(nameof(WarmUpComposeAsync));

            if (!HasNamedProfiles())
            {
                // XamlRoot が利用可能になるまで待つ（Content.Loaded は Activate() 後に発火）
                var tcs = new TaskCompletionSource();
                ((FrameworkElement)Content).Loaded += (_, _) => tcs.TrySetResult();
                if (((FrameworkElement)Content).IsLoaded) tcs.TrySetResult();
                await tcs.Task;

                ProfileConfig? createdProfile = null;
                OnboardingWindow.ShowModal(this, _appSettings,
                    onCreated: profile =>
                    {
                        createdProfile = profile;
                        _profiles.Add(profile);
                        SaveProfiles();
                        RefreshAllProfileBadges();
                        UpdateHasNamedProfiles();
                    },
                    onClosed: () =>
                    {
                        // ウィンドウが閉じて WebView2 env が解放された後にタイムラインを追加
                        if (createdProfile is not null)
                        {
                            var cfg = CreateDefaultConfig(HomeTimelineUrl);
                            cfg.ProfileId = createdProfile.Id;
                            AddTimeline(cfg);
                        }
                        // オンボーディングで変更した既定値を永続化
                        SaveSettings();
                    });
            }
        }

        /// <summary>名前付きプロファイルが1つ以上あるか。</summary>
        private bool HasNamedProfiles()
            => _profiles.Any(p => p.Id != "default");

        /// <summary>ViewModel.HasNamedProfiles を現在のプロファイル状態で更新する。</summary>
        private void UpdateHasNamedProfiles()
            => ViewModel.HasNamedProfiles = HasNamedProfiles();
    }
}
