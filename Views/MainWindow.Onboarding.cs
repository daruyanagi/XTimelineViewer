using Microsoft.UI.Xaml;
using System.Linq;
using System.Threading.Tasks;

using XTimelineViewer.Models;

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
            await RestoreTimelinesAsync();
            UpdateHasNamedProfiles();

            if (!HasNamedProfiles())
            {
                // XamlRoot が利用可能になるまで待つ（Content.Loaded は Activate() 後に発火）
                var tcs = new TaskCompletionSource();
                ((FrameworkElement)Content).Loaded += (_, _) => tcs.TrySetResult();
                if (((FrameworkElement)Content).IsLoaded) tcs.TrySetResult();
                await tcs.Task;

                OnboardingWindow.ShowModal(this, profile =>
                {
                    _profiles.Add(profile);
                    SaveProfiles();
                    RefreshAllProfileBadges();
                    UpdateHasNamedProfiles();

                    // ホームタイムラインを自動追加
                    AddTimeline(new TimelineConfig
                    {
                        Url       = HomeTimelineUrl,
                        ProfileId = profile.Id,
                    });
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
