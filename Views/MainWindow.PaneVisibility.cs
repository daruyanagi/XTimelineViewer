using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.Foundation;

using XTimelineViewer.Views.Controls;

namespace XTimelineViewer.Views
{
    public sealed partial class MainWindow : Window
    {
        // ── ペイン WebView2 の可視制御（#418）─────────────────────────────
        //
        // WinUI 3 の WebView2 は、コントロール 1 個につき msedgewebview2.exe 側に
        // トップレベルの WS_POPUP（Chrome_WidgetWin_1）を 1 枚持ち、それを
        // コントロールの「画面絶対座標」へ置く。中身は DirectComposition で
        // アプリのビジュアルツリーへ合成されるので ScrollViewer のクリップは
        // 見た目には効くが、この POPUP 自体は移動もクリップもされない。
        // 結果、ウィンドウ外へはみ出したペインの矩形がヒットテストを奪い、
        // その下のデスクトップがクリックできなくなる（#418）。
        //
        // Opacity=0 や IsHitTestVisible=false ではこの POPUP は消えない。
        // 消せるのは Visibility.Collapsed だけ。投稿ダイアログ（#244）が
        // ペインを Collapsed にしているのも、根は同じ現象（z-order）。
        //
        // 隠すのは「ビューポートから完全に外れたペイン」だけにする。
        // 端にまたがっているペインまで隠すと、右端・左端の途中まで見えている列が
        // まるごと空白になり、実際に試したところ使い勝手を大きく損なった。
        //
        // したがって、またがっている 1 枚ぶん（最大でペイン幅）は画面外へ残る。
        // ペイン数に比例して無制限に広がる部分は消えるので、実害の大きいところは
        // これで塞げる。残りを消すには WebView2 の HWND 自体をクリップするしかなく、
        // それは別プロセスのウィンドウを触ることになるので採らない。

        /// <summary>判定の遊び。レイアウトの丸め誤差で端のペインが点滅するのを防ぐ。</summary>
        private const double PaneVisibilityTolerance = 0.5;

        /// <summary>
        /// ダイアログ表示中など、位置に関係なく全ペインの WebView2 を隠している間は true。
        /// </summary>
        private bool _paneWebViewsSuppressed;

        /// <summary>
        /// 投稿・検索ダイアログのように、ペインの上へ別の WebView2 を重ねる間だけ
        /// 全ペインを隠す。以前は呼び出し側が Panes を直接舐めて Visible へ戻していたが、
        /// それだと <see cref="UpdatePaneWebViewVisibility"/> の判定と食い違う。
        /// </summary>
        private void SuppressPaneWebViews()
        {
            _paneWebViewsSuppressed = true;
            UpdatePaneWebViewVisibility();
        }

        /// <summary>ダイアログを閉じたあと、位置による判定へ戻す。</summary>
        private void ResumePaneWebViews()
        {
            _paneWebViewsSuppressed = false;
            UpdatePaneWebViewVisibility();
        }

        /// <summary>
        /// 各ペインの WebView2 を、ビューポートに少しでも掛かっているかどうかで出し入れする。
        ///
        /// 呼び出し漏れを避けるため、個々の変更点（追加・削除・並べ替え・幅変更・拡大）を
        /// 追いかけるのではなく、横スクロール（ViewChanged）とレイアウト確定
        /// （LayoutUpdated）の 2 か所からだけ呼ぶ。値が変わるときしか代入しないので、
        /// LayoutUpdated から呼んでもレイアウトのループにはならない。
        /// </summary>
        private void UpdatePaneWebViewVisibility()
        {
            var viewport = TimelineScroll.ViewportWidth;

            foreach (var pane in Panes)
            {
                var want = ShouldShowPaneWebView(pane, viewport)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                if (pane.WebView.Visibility != want)
                    pane.WebView.Visibility = want;
            }
        }

        private bool ShouldShowPaneWebView(TimelinePane pane, double viewport)
        {
            if (_paneWebViewsSuppressed) return false;

            // ペインごと隠されている（メディア拡大中の他ペイン #287）なら位置は見ない。
            if (pane.Visibility != Visibility.Visible) return false;

            // CoreWebView2 の生成が終わるまでは隠さない。Collapsed のままだと
            // EnsureCoreWebView2Async が完了せず、初回のナビゲートごと止まってしまう。
            if (pane.WebView.CoreWebView2 is null) return true;

            // ビューポート基準の座標。TransformToVisual は横スクロール量を含む。
            var left  = pane.TransformToVisual(TimelineScroll).TransformPoint(new Point(0, 0)).X;
            var right = left + pane.ActualWidth;

            // 少しでも掛かっていれば出す。完全に外れたときだけ隠す。
            return right > PaneVisibilityTolerance
                && left  < viewport - PaneVisibilityTolerance;
        }

        private void TimelineScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
            => UpdatePaneWebViewVisibility();

        private void TimelinePanel_LayoutUpdated(object? sender, object e)
            => UpdatePaneWebViewVisibility();
    }
}
