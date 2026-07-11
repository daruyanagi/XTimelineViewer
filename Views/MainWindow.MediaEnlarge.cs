using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;

namespace XTimelineViewer.Views
{
    public sealed partial class MainWindow : Window
    {
        // 画像表示中のペインの一時拡大（試験機能 #287）。
        //
        // WebView2 の親要素（ペイン）は変更しない。visual tree を移し替えると
        // CoreWebView2 が作り直され、開いていたライトボックスや再生中の動画が
        // リセットされてしまう（#32 の失敗と同じ問題）。代わりに、対象ペインが
        // 属する横並び StackPanel（TimelinePanel）内で、他のペインを一時的に
        // 隠し、対象ペインの幅だけ表示領域いっぱいに広げる。

        /// <summary>対象ペインを一時拡大する。他のペインは一時的に非表示になる。</summary>
        private void EnlargePane(Grid pane)
        {
            if (_enlargedPane == pane) return;

            _enlargedPane = pane;
            foreach (var p in TimelinePanel.Children.OfType<Grid>())
                p.Visibility = p == pane ? Visibility.Visible : Visibility.Collapsed;

            UpdateEnlargedPaneWidth();
        }

        /// <summary>拡大表示を解除し、全ペインの表示状態と幅を元に戻す。</summary>
        private void RestorePaneSize()
        {
            if (_enlargedPane is null) return;

            var pane = _enlargedPane;
            _enlargedPane = null;

            foreach (var p in TimelinePanel.Children.OfType<Grid>())
                p.Visibility = Visibility.Visible;

            if (_paneToConfig.TryGetValue(pane, out var cfg))
                pane.Width = cfg.Width;
        }

        /// <summary>拡大中のペイン幅を、表示領域（TimelineScroll のビューポート）いっぱいに合わせる。</summary>
        private void UpdateEnlargedPaneWidth()
        {
            if (_enlargedPane is null) return;

            var target = TimelineScroll.ActualWidth
                         - TimelinePanel.Padding.Left - TimelinePanel.Padding.Right
                         - _enlargedPane.Margin.Left - _enlargedPane.Margin.Right;
            if (target > 0)
                _enlargedPane.Width = target;
        }

        private void TimelineScroll_SizeChanged(object sender, SizeChangedEventArgs e)
            => UpdateEnlargedPaneWidth();
    }
}
