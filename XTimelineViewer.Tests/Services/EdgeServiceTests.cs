using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services;

public class EdgeServiceTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public EdgeServiceTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose()     => Directory.Delete(_tempDir, recursive: true);

    private string At(string filename) => Path.Combine(_tempDir, filename);

    // ── FindEdgePath ──────────────────────────────────────────────────────────

    [Fact]
    public void FindEdgePath_ReturnsNullOrValidPath()
    {
        var path = EdgeService.FindEdgePath();
        // Edge がインストールされていれば有効なパス、なければ null
        if (path is not null)
            Assert.True(File.Exists(path));
    }

    // ── EnumerateProfiles ─────────────────────────────────────────────────────

    [Fact]
    public void EnumerateProfiles_ReturnsListWithoutException()
    {
        // Edge がインストールされていなくても例外を投げない
        var profiles = EdgeService.EnumerateProfiles();
        Assert.NotNull(profiles);
    }

    [Fact]
    public void EnumerateProfiles_WhenEdgeInstalled_ContainsDefault()
    {
        // Edge がインストールされている環境でのみ意味のあるテスト
        var userDataPath = EdgeService.GetUserDataPath();
        if (!Directory.Exists(userDataPath))
            return; // skip — Edge not installed

        var profiles = EdgeService.EnumerateProfiles();
        Assert.True(profiles.Count > 0, "Edge がインストールされていればプロファイルが 1 つ以上あるはず");
        Assert.Contains(profiles, p => p.Directory == "Default");
    }

    [Fact]
    public void EnumerateProfiles_ProfileHasDirectoryAndDisplayName()
    {
        var userDataPath = EdgeService.GetUserDataPath();
        if (!Directory.Exists(userDataPath))
            return; // skip

        var profiles = EdgeService.EnumerateProfiles();
        foreach (var p in profiles)
        {
            Assert.False(string.IsNullOrEmpty(p.Directory));
            Assert.False(string.IsNullOrEmpty(p.DisplayName));
        }
    }

    // ── EdgeProfile record ────────────────────────────────────────────────────

    [Fact]
    public void EdgeProfile_RecordEquality()
    {
        var a = new EdgeProfile("Default", "Profile 1", "user@example.com");
        var b = new EdgeProfile("Default", "Profile 1", "user@example.com");
        Assert.Equal(a, b);
    }

    [Fact]
    public void EdgeProfile_RecordInequality()
    {
        var a = new EdgeProfile("Default", "Profile 1", "user@example.com");
        var b = new EdgeProfile("Profile 1", "Profile 2", "other@example.com");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EdgeProfile_UserNameCanBeEmpty()
    {
        var p = new EdgeProfile("Profile 2", "プロファイル 3", "");
        Assert.Equal("", p.UserName);
    }

    // ── AppSettings の新規プロパティ ───────────────────────────────────────────

    [Fact]
    public void AppSettings_ExternalBrowserDefaults()
    {
        var s = new XTimelineViewer.Models.AppSettings();
        Assert.Equal("system", s.ExternalBrowser);
        Assert.Equal("",       s.EdgeProfileDirectory);
    }

    [Fact]
    public void AppSettings_ExternalBrowser_RoundTrip()
    {
        var path = At("settings.json");
        var original = new XTimelineViewer.Models.AppSettings
        {
            ExternalBrowser      = "edge",
            EdgeProfileDirectory = "Profile 1"
        };

        SettingsService.SaveSettings(path, original);
        var loaded = SettingsService.LoadSettings(path);

        Assert.Equal("edge",      loaded.ExternalBrowser);
        Assert.Equal("Profile 1", loaded.EdgeProfileDirectory);
    }
}
