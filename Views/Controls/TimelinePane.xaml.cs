using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using System;
using XTimelineViewer.Models;
using XTimelineViewer.Services;

namespace XTimelineViewer.Views.Controls
{
    /// <summary>
    /// タイムライン 1 本分のペイン（#345）。
    ///
    /// 以前は MainWindow.AddTimeline() の中でこの視覚ツリーをコードで組み立てており、
    /// ペイン単位の状態を MainWindow が複数の辞書で手持ちしていた。作る側と作り直す側が
    /// 別々に存在するせいで、同じ定数を片方だけ直す事故が繰り返し起きている
    /// （#337 / #341 のバッジ重複、#359 の番号ずれ、#362 の後始末漏れ）。
    ///
    /// 段階 2A では「視覚ツリーの宣言」だけをここへ移す。振る舞い（フォーカス、
    /// ⚙ ダイアログ、テーマ適用など）は MainWindow に残っており、段階 2B で移す。
    /// </summary>
    public sealed partial class TimelinePane : UserControl
    {
        /// <summary>このペインが表示しているタイムラインの設定。</summary>
        internal TimelineConfig Config { get; }

        internal TimelinePane(TimelineConfig config)
        {
            InitializeComponent();
            Config = config;
            _webView = WebViewHost;

            Width = config.Width;
            TypeIcon.Glyph = UrlHelper.GetTimelineGlyph(config.Url);
            HardReloadTooltip = new ToolTip();
            ToolTipService.SetToolTip(TypeIcon, HardReloadTooltip);
            AutoLoadTooltip = new ToolTip { Content = R.Get("AutoLoad_Off") };
            ToolTipService.SetToolTip(AutoLoadIcon, AutoLoadTooltip);
            RefreshLocalizedText();
            UpdateUrlHeader();
        }

        // ── 外から触る要素 ────────────────────────────────────────────────────
        // 段階 2B でこの多くは private になり、代わりにメソッドを生やす想定。

        /// <summary>ヘッダー。クリックでのフォーカス、ドラッグでの並べ替えに使う。</summary>
        public Grid Header => HeaderGrid;

        /// <summary>枠・背景を塗る対象。UserControl 自身ではなく内側の Grid。</summary>
        public Grid Root => PaneRoot;

        // プロファイルを切り替えると、別の user data folder の環境で
        // WebView2 を作り直す必要がある。XAML の WebViewHost は初回の実体で、
        // 以降は ReplaceWebView() が差し替える。
        private WebView2 _webView;

        public WebView2 WebView => _webView;

        /// <summary>
        /// WebView2 を新しいインスタンスへ差し替えて返す。
        /// 差し替えの手順をここに集めることで、呼び出し側の手順漏れを防ぐ（#361）。
        /// イベントの再購読は呼び出し元の責任（段階 2B でこちらへ移す）。
        /// </summary>
        internal WebView2 ReplaceWebView()
        {
            PaneRoot.Children.Remove(_webView);
            _webView = new WebView2
            {
                VerticalAlignment   = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            Grid.SetRow(_webView, 1);
            PaneRoot.Children.Add(_webView);
            AutomationProperties.SetName(_webView, UrlLabel.Text);
            return _webView;
        }
        public TextBlock NumberLabel => NumberLabelText;
        public FontIcon AutoLoadIndicatorIcon => AutoLoadIcon;

        /// <summary>種別アイコンに付くツールチップ。ハードリロードの残り時間を出す。</summary>
        public ToolTip HardReloadTooltip { get; }

        /// <summary>自動更新インジケーターのツールチップ。</summary>
        public ToolTip AutoLoadTooltip { get; }

        public Button SettingsButton => SettingsBtn;

        /// <summary>ホームタイムラインかどうか。自動更新（#207）の対象判定に使う。</summary>
        public bool IsHome => UrlHelper.IsHomeUrl(Config.Url);

        // ── 表示の更新 ────────────────────────────────────────────────────────

        /// <summary>
        /// Config.Url の変更をヘッダーへ反映する。ベース URL の変更やリスト URL の
        /// ライブ解決（#211）から呼ばれる。
        /// </summary>
        public void UpdateUrlHeader()
        {
            UrlLabel.Text = Uri.TryCreate(Config.Url, UriKind.Absolute, out var u)
                ? SearchQueryHelper.DecodeSearchPath(u.Host + u.PathAndQuery)
                : Config.Url;
            TypeIcon.Glyph = UrlHelper.GetTimelineGlyph(Config.Url);
            AutomationProperties.SetName(_webView, UrlLabel.Text);
            AutoLoadIcon.Visibility = IsHome ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>言語切り替え（#117）で呼び直す。</summary>
        public void RefreshLocalizedText()
        {
            var tip = R.Get("Pane_Settings_Tooltip");
            ToolTipService.SetToolTip(SettingsBtn, tip);
            AutomationProperties.SetName(SettingsBtn, tip);
        }

        /// <summary>
        /// 番号バッジ（#225）。表示順の 1..9 を受け取る。9 を超えるペインは null。
        /// AutomationId も振り直す（ui-smoke.ps1 が連番を検査するため）。
        /// </summary>
        public void SetNumber(int? oneBased)
        {
            if (oneBased is int n)
            {
                NumberLabelText.Text       = $"{n}.";
                NumberLabelText.Visibility = Visibility.Visible;
                ToolTipService.SetToolTip(NumberLabelText, string.Format(R.Get("Tooltip_ActivateHotkey"), n));
                AutomationProperties.SetAutomationId(NumberLabelText, $"PaneNumber{n}");
            }
            else
            {
                NumberLabelText.Text       = string.Empty;
                NumberLabelText.Visibility = Visibility.Collapsed;
                ToolTipService.SetToolTip(NumberLabelText, null);
                AutomationProperties.SetAutomationId(NumberLabelText, string.Empty);
            }
        }

        /// <summary>
        /// プロファイルバッジ。以前は Border ごと作り直して差し替えていたため、
        /// 列番号が生成側と再生成側の 2 か所に手書きされていた（#337）。
        /// 器を固定して中身だけ差し替える。
        /// </summary>
        public void SetProfileBadge(string text, Brush background, Brush foreground, bool bordered, bool visible)
        {
            ProfileBadge.Background      = background;
            ProfileBadge.BorderBrush     = bordered ? foreground : null;
            ProfileBadge.BorderThickness = new Thickness(bordered ? 1 : 0);
            ProfileBadge.Visibility      = visible ? Visibility.Visible : Visibility.Collapsed;
            ProfileBadgeText.Text        = text;
            ProfileBadgeText.Foreground  = foreground;
        }

        /// <summary>自動更新インジケーター（#207）の見た目を更新する。</summary>
        public void SetAutoLoadIndicator(string glyph, double opacity, string tooltip)
        {
            AutoLoadIcon.Glyph   = glyph;
            AutoLoadIcon.Opacity = opacity;
            AutoLoadTooltip.Content = tooltip;
        }

        /// <summary>自動更新インジケーターの表示・非表示。ホームペインのみ表示する。</summary>
        public void SetAutoLoadIndicatorVisible(bool visible)
            => AutoLoadIcon.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
