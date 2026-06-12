using System.Collections.Generic;
using XTimelineViewer.Models;
using XTimelineViewer.ViewModels;
using Xunit;

namespace XTimelineViewer.Tests.ViewModels;

public class SettingsViewModelTests
{
    // ── ThemeIndex ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Default", 0)]
    [InlineData("Light",   1)]
    [InlineData("Dark",    2)]
    [InlineData("bogus",   0)] // 不正値は既定（システム）にフォールバック
    public void ThemeIndex_Get_MapsFromSettings(string theme, int expected)
    {
        var vm = new SettingsViewModel(new AppSettings { Theme = theme });
        Assert.Equal(expected, vm.ThemeIndex);
    }

    [Fact]
    public void ThemeIndex_Set_UpdatesSettingsAndNotifies()
    {
        var s = new AppSettings();
        var notified = 0;
        var vm = new SettingsViewModel(s, () => notified++);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ThemeIndex = 2;

        Assert.Equal("Dark", s.Theme);
        Assert.Equal(1, notified);
        Assert.Contains(nameof(SettingsViewModel.ThemeIndex), raised);
    }

    [Theory]
    [InlineData(-1)] // ItemsSource 再設定時の一時値
    [InlineData(3)]
    public void ThemeIndex_Set_OutOfRange_Ignored(int value)
    {
        var s = new AppSettings { Theme = "Dark" };
        var notified = 0;
        var vm = new SettingsViewModel(s, () => notified++);

        vm.ThemeIndex = value;

        Assert.Equal("Dark", s.Theme);
        Assert.Equal(0, notified);
    }

    [Fact]
    public void ThemeIndex_Set_SameValue_DoesNotNotify()
    {
        var s = new AppSettings { Theme = "Light" };
        var notified = 0;
        var vm = new SettingsViewModel(s, () => notified++);

        vm.ThemeIndex = 1;

        Assert.Equal(0, notified);
    }

    // ── LanguageIndex ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("system", 0)]
    [InlineData("ja-JP",  1)]
    [InlineData("en-US",  2)]
    public void LanguageIndex_RoundTrip(string lang, int index)
    {
        var s = new AppSettings { Language = lang };
        var vm = new SettingsViewModel(s);
        Assert.Equal(index, vm.LanguageIndex);

        var s2 = new AppSettings();
        var vm2 = new SettingsViewModel(s2);
        vm2.LanguageIndex = index;
        Assert.Equal(lang, s2.Language);
    }

    [Fact]
    public void LanguageIndex_SettingsChangedBeforePropertyChanged()
    {
        // 言語変更時はリソース再読込（settingsChanged）→ ページ再構築（PropertyChanged）の
        // 順序が必須。逆になると旧言語で再構築される。
        var order = new List<string>();
        var s = new AppSettings();
        SettingsViewModel vm = null!;
        vm = new SettingsViewModel(s, () => order.Add("settingsChanged"));
        vm.PropertyChanged += (_, _) => order.Add("propertyChanged");

        vm.LanguageIndex = 2;

        Assert.Equal(["settingsChanged", "propertyChanged"], order);
    }

    // ── タイムラインの既定値（反転ロジック） ───────────────────────────────────

    [Fact]
    public void ShowSidebarByDefault_InvertsHideFlag()
    {
        var s = new AppSettings { DefaultHideSidebar = true };
        var vm = new SettingsViewModel(s);

        Assert.False(vm.ShowSidebarByDefault);

        vm.ShowSidebarByDefault = true;
        Assert.False(s.DefaultHideSidebar);
    }

    [Fact]
    public void ShowComposeByDefault_InvertsHideFlag()
    {
        var s = new AppSettings(); // DefaultHideCompose = true が既定
        var vm = new SettingsViewModel(s);

        Assert.False(vm.ShowComposeByDefault);

        vm.ShowComposeByDefault = true;
        Assert.False(s.DefaultHideCompose);
    }

    [Fact]
    public void ShowListHeaderByDefault_InvertsHideFlag()
    {
        var s = new AppSettings();
        var vm = new SettingsViewModel(s);

        Assert.True(vm.ShowListHeaderByDefault);

        vm.ShowListHeaderByDefault = false;
        Assert.True(s.DefaultHideListHeader);
    }

    // ── 試験機能 ──────────────────────────────────────────────────────────────

    [Fact]
    public void AutoActivateMinutes_ClampsToRange()
    {
        var s = new AppSettings();
        var vm = new SettingsViewModel(s);

        vm.AutoActivateMinutes = 999;
        Assert.Equal(60, s.AutoActivateMinutes);

        vm.AutoActivateMinutes = -5;
        Assert.Equal(0, s.AutoActivateMinutes);
    }

    [Fact]
    public void AutoActivateMinutes_NaN_Ignored()
    {
        var s = new AppSettings { AutoActivateMinutes = 10 };
        var notified = 0;
        var vm = new SettingsViewModel(s, () => notified++);

        vm.AutoActivateMinutes = double.NaN;

        Assert.Equal(10, s.AutoActivateMinutes);
        Assert.Equal(0, notified);
    }

    [Theory]
    [InlineData("system", 0, false)]
    [InlineData("edge",   1, true)]
    public void BrowserIndex_MapsAndReportsEdgeSelection(string browser, int index, bool isEdge)
    {
        var s = new AppSettings { ExternalBrowser = browser };
        var vm = new SettingsViewModel(s);

        Assert.Equal(index, vm.BrowserIndex);
        Assert.Equal(isEdge, vm.IsEdgeSelected);
    }

    [Fact]
    public void BrowserIndex_Set_RaisesIsEdgeSelected()
    {
        var s = new AppSettings();
        var vm = new SettingsViewModel(s);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.BrowserIndex = 1;

        Assert.Equal("edge", s.ExternalBrowser);
        Assert.Contains(nameof(SettingsViewModel.BrowserIndex),    raised);
        Assert.Contains(nameof(SettingsViewModel.IsEdgeSelected),  raised);
    }

    [Fact]
    public void ExperimentalToggles_UpdateSettings()
    {
        var s = new AppSettings();
        var vm = new SettingsViewModel(s);

        vm.OpenComposerInBrowser  = true;
        vm.OpenTimestampInBrowser = true;
        vm.ShowAutoActivateLabel  = true;

        Assert.True(s.OpenComposerInBrowser);
        Assert.True(s.OpenTimestampInBrowser);
        Assert.True(s.ShowAutoActivateLabel);
    }

    [Fact]
    public void ShowToggles_SameValue_DoesNotNotify()
    {
        var s = new AppSettings();
        var notified = 0;
        var vm = new SettingsViewModel(s, () => notified++);

        vm.ShowSidebarByDefault    = !s.DefaultHideSidebar;
        vm.ShowComposeByDefault    = !s.DefaultHideCompose;
        vm.ShowListHeaderByDefault = !s.DefaultHideListHeader;

        Assert.Equal(0, notified);
    }
}
