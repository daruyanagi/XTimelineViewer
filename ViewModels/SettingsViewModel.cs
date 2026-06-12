using System;
using CommunityToolkit.Mvvm.ComponentModel;
using XTimelineViewer.Models;

namespace XTimelineViewer.ViewModels
{
    /// <summary>
    /// 設定ページ用 ViewModel (#199)。AppSettings をラップして双方向 x:Bind を提供する。
    /// UI 非依存（Microsoft.UI.Xaml を参照しない）に保ち、ユニットテスト可能にする。
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        internal static readonly string[] ThemeValues = ["Default", "Light", "Dark"];
        internal static readonly string[] LangValues  = ["system", "ja-JP", "en-US"];

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
    }
}
