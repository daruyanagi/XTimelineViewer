using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using XTimelineViewer.Models;

namespace XTimelineViewer.ViewModels
{
    /// <summary>保存済み検索クエリの表示用アイテム。x:Bind から参照される。</summary>
    public sealed class SavedQueryItem(string path, string decoded, string deleteLabel)
    {
        public string Path        { get; set; } = path;
        public string Decoded     { get; set; } = decoded;
        public string DeleteLabel { get; set; } = deleteLabel;
    }

    /// <summary>
    /// 設定ページ用 ViewModel (#199)。AppSettings をラップして双方向 x:Bind を提供する。
    /// UI 非依存（Microsoft.UI.Xaml を参照しない）に保ち、ユニットテスト可能にする。
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        internal static readonly string[] ThemeValues   = ["Default", "Light", "Dark"];
        internal static readonly string[] LangValues    = ["system", "ja-JP", "en-US"];
        internal static readonly string[] BrowserValues = ["system", "edge"];

        private readonly AppSettings _settings;
        private readonly Action?     _settingsChanged;

        public SettingsViewModel(AppSettings settings, Action? settingsChanged = null)
        {
            _settings        = settings;
            _settingsChanged = settingsChanged;
        }

        private void Notify(string propertyName)
        {
            // 先に設定変更を通知（保存・R.Reload 等）してから PropertyChanged を発火する。
            // 言語変更時、PropertyChanged 購読側（ページ再構築）が新しいリソースを参照できるようにするため。
            _settingsChanged?.Invoke();
            OnPropertyChanged(propertyName);
        }

        // ── テーマ / 言語（ComboBox の SelectedIndex に対応） ──────────────────────

        public int ThemeIndex
        {
            get => Math.Max(0, Array.IndexOf(ThemeValues, _settings.Theme));
            set
            {
                // ItemsSource 再設定時に SelectedIndex が一時的に -1 になるため範囲外は無視する
                if (value < 0 || value >= ThemeValues.Length) return;
                if (_settings.Theme == ThemeValues[value]) return;
                _settings.Theme = ThemeValues[value];
                Notify(nameof(ThemeIndex));
            }
        }

        public int LanguageIndex
        {
            get => Math.Max(0, Array.IndexOf(LangValues, _settings.Language));
            set
            {
                if (value < 0 || value >= LangValues.Length) return;
                if (_settings.Language == LangValues[value]) return;
                _settings.Language = LangValues[value];
                Notify(nameof(LanguageIndex));
            }
        }

        // ── タイムラインの既定値（ToggleSwitch.IsOn = 表示 なので Hide* を反転） ────

        public bool ShowSidebarByDefault
        {
            get => !_settings.DefaultHideSidebar;
            set
            {
                if (_settings.DefaultHideSidebar == !value) return;
                _settings.DefaultHideSidebar = !value;
                Notify(nameof(ShowSidebarByDefault));
            }
        }

        public bool ShowComposeByDefault
        {
            get => !_settings.DefaultHideCompose;
            set
            {
                if (_settings.DefaultHideCompose == !value) return;
                _settings.DefaultHideCompose = !value;
                Notify(nameof(ShowComposeByDefault));
            }
        }

        public bool ShowListHeaderByDefault
        {
            get => !_settings.DefaultHideListHeader;
            set
            {
                if (_settings.DefaultHideListHeader == !value) return;
                _settings.DefaultHideListHeader = !value;
                Notify(nameof(ShowListHeaderByDefault));
            }
        }

        // ── 試験機能 ──────────────────────────────────────────────────────────────

        public bool OpenComposerInBrowser
        {
            get => _settings.OpenComposerInBrowser;
            set
            {
                if (_settings.OpenComposerInBrowser == value) return;
                _settings.OpenComposerInBrowser = value;
                Notify(nameof(OpenComposerInBrowser));
            }
        }

        public bool OpenTimestampInBrowser
        {
            get => _settings.OpenTimestampInBrowser;
            set
            {
                if (_settings.OpenTimestampInBrowser == value) return;
                _settings.OpenTimestampInBrowser = value;
                Notify(nameof(OpenTimestampInBrowser));
            }
        }

        /// <summary>ホーム自動更新（#207）の ON/OFF。</summary>
        public bool HomeAutoLoadEnabled
        {
            get => _settings.HomeAutoLoadEnabled;
            set
            {
                if (_settings.HomeAutoLoadEnabled == value) return;
                _settings.HomeAutoLoadEnabled = value;
                Notify(nameof(HomeAutoLoadEnabled));
            }
        }

        /// <summary>ホーム自動更新の間隔（秒）。NumberBox.Value（double）にバインド。5–60 に丸める。</summary>
        public double HomeAutoLoadIntervalSeconds
        {
            get => _settings.HomeAutoLoadIntervalSeconds;
            set
            {
                if (double.IsNaN(value)) return;
                var sec = (int)Math.Clamp(value, 5, 60);
                if (_settings.HomeAutoLoadIntervalSeconds == sec) return;
                _settings.HomeAutoLoadIntervalSeconds = sec;
                Notify(nameof(HomeAutoLoadIntervalSeconds));
            }
        }

        /// <summary>投稿ウィンドウのプリロード（試験機能 #244 案B）。</summary>
        public bool ComposePreloadEnabled
        {
            get => _settings.ComposePreloadEnabled;
            set
            {
                if (_settings.ComposePreloadEnabled == value) return;
                _settings.ComposePreloadEnabled = value;
                Notify(nameof(ComposePreloadEnabled));
            }
        }

        /// <summary>投稿後にプライマリプロフィールへ戻す（試験機能 #285）。</summary>
        public bool ComposeResetToPrimaryEnabled
        {
            get => _settings.ComposeResetToPrimaryEnabled;
            set
            {
                if (_settings.ComposeResetToPrimaryEnabled == value) return;
                _settings.ComposeResetToPrimaryEnabled = value;
                Notify(nameof(ComposeResetToPrimaryEnabled));
            }
        }

        /// <summary>画像表示中のペインを一時拡大する（試験機能 #287）。</summary>
        public bool MediaEnlargeEnabled
        {
            get => _settings.MediaEnlargeEnabled;
            set
            {
                if (_settings.MediaEnlargeEnabled == value) return;
                _settings.MediaEnlargeEnabled = value;
                Notify(nameof(MediaEnlargeEnabled));
            }
        }

        /// <summary>動画の全画面ボタンでペインを一時拡大する（試験機能 #289）。</summary>
        public bool VideoEnlargeEnabled
        {
            get => _settings.VideoEnlargeEnabled;
            set
            {
                if (_settings.VideoEnlargeEnabled == value) return;
                _settings.VideoEnlargeEnabled = value;
                Notify(nameof(VideoEnlargeEnabled));
            }
        }

        public int BrowserIndex
        {
            get => Math.Max(0, Array.IndexOf(BrowserValues, _settings.ExternalBrowser));
            set
            {
                if (value < 0 || value >= BrowserValues.Length) return;
                if (_settings.ExternalBrowser == BrowserValues[value]) return;
                _settings.ExternalBrowser = BrowserValues[value];
                Notify(nameof(BrowserIndex));
                OnPropertyChanged(nameof(IsEdgeSelected));
            }
        }

        /// <summary>Edge プロファイル ComboBox の有効/無効判定に使う。</summary>
        public bool IsEdgeSelected => _settings.ExternalBrowser == "edge";

        // ── 保存済み検索クエリ（ユーザーデータ管理ページ） ─────────────────────────

        public ObservableCollection<SavedQueryItem> SavedQueries { get; } = [];

        /// <summary>保存済みクエリが 1 件以上あるか。Expander の有効/無効判定に使う。</summary>
        public bool HasSavedQueries => SavedQueries.Count > 0;

        /// <summary>AppSettings から保存済みクエリ一覧を再構築する。削除ラベルは表示言語に依存するため引数で受け取る。</summary>
        public void ReloadSavedQueries(string deleteLabel)
        {
            SavedQueries.Clear();
            foreach (var path in _settings.SavedSearchQueries)
                SavedQueries.Add(new SavedQueryItem(path, Uri.UnescapeDataString(path), deleteLabel));
            OnPropertyChanged(nameof(HasSavedQueries));
        }

        public void RemoveSavedQuery(string path)
        {
            _settings.SavedSearchQueries.Remove(path);
            var item = SavedQueries.FirstOrDefault(q => q.Path == path);
            if (item is not null) SavedQueries.Remove(item);
            _settingsChanged?.Invoke();
            OnPropertyChanged(nameof(HasSavedQueries));
        }
    }
}
