using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace XTimelineViewer.ViewModels
{
    /// <summary>
    /// MainWindow の ViewModel（フェーズ1）。
    /// 現時点ではタイムライン有無を管理し、UI の表示状態を制御する。
    /// _configs / _appSettings / _profiles の移動はフェーズ2（#108）で対応予定。
    /// </summary>
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ── タイムラインの有無 ────────────────────────────────────────────────────

        private bool _hasTimelines;

        /// <summary>タイムラインが1件以上存在するとき true。</summary>
        public bool HasTimelines
        {
            get => _hasTimelines;
            set
            {
                if (_hasTimelines == value) return;
                _hasTimelines = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(TimelineScrollVisibility));
                NotifyPropertyChanged(nameof(DropHintBorderVisibility));
            }
        }

        /// <summary>タイムラインスクロールエリアの Visibility。タイムラインがあれば Visible。</summary>
        public Visibility TimelineScrollVisibility
            => HasTimelines ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>空状態ヒントの Visibility。タイムラインがなければ Visible。</summary>
        public Visibility DropHintBorderVisibility
            => HasTimelines ? Visibility.Collapsed : Visibility.Visible;

        // ── 名前付きプロファイルの有無 ──────────────────────────────────────────────

        private bool _hasNamedProfiles;

        /// <summary>名前付きプロファイルが1件以上存在するとき true。投稿ボタンの有効/無効に使う。</summary>
        public bool HasNamedProfiles
        {
            get => _hasNamedProfiles;
            set
            {
                if (_hasNamedProfiles == value) return;
                _hasNamedProfiles = value;
                NotifyPropertyChanged();
            }
        }
    }
}
