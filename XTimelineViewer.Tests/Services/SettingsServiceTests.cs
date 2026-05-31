using System;
using System.Collections.Generic;
using System.IO;
using XTimelineViewer;
using XTimelineViewer.Services;
using Xunit;

namespace XTimelineViewer.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public SettingsServiceTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose()         => Directory.Delete(_tempDir, recursive: true);

    private string At(string filename) => Path.Combine(_tempDir, filename);

    // ── LoadSettings ──────────────────────────────────────────────────────────

    [Fact]
    public void LoadSettings_MissingFile_ReturnsDefaults()
    {
        var s = SettingsService.LoadSettings(At("settings.json"));
        Assert.NotNull(s);
        Assert.Equal("Default", s.Theme);
        Assert.Equal("system",  s.Language);
    }

    [Fact]
    public void LoadSettings_ValidJson_ReturnsCorrectValues()
    {
        File.WriteAllText(At("settings.json"),
            """{"Theme":"Dark","Language":"ja-JP","AutoActivateMinutes":5}""");

        var s = SettingsService.LoadSettings(At("settings.json"));
        Assert.Equal("Dark",  s.Theme);
        Assert.Equal("ja-JP", s.Language);
        Assert.Equal(5,       s.AutoActivateMinutes);
    }

    [Fact]
    public void LoadSettings_InvalidJson_ReturnsDefaults()
    {
        File.WriteAllText(At("settings.json"), "not { valid json }");

        var s = SettingsService.LoadSettings(At("settings.json"));
        Assert.NotNull(s);
        Assert.Equal("Default", s.Theme);
    }

    [Fact]
    public void LoadSettings_AlwaysDisablesSeparateComposeEnv()
    {
        File.WriteAllText(At("settings.json"), """{"SeparateComposeEnv":true}""");

        var s = SettingsService.LoadSettings(At("settings.json"));
        Assert.False(s.SeparateComposeEnv); // 廃止予定: 強制無効化 (#17)
    }

    // ── SaveSettings / Round-trip ─────────────────────────────────────────────

    [Fact]
    public void SaveSettings_CreatesFileAndParentDirectory()
    {
        var path = Path.Combine(_tempDir, "sub", "settings.json");

        SettingsService.SaveSettings(path, new AppSettings());

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SaveAndLoadSettings_RoundTrip()
    {
        var path = At("settings.json");
        var original = new AppSettings
        {
            Theme = "Light", Language = "en-US", AutoActivateMinutes = 10
        };

        SettingsService.SaveSettings(path, original);
        var loaded = SettingsService.LoadSettings(path);

        Assert.Equal("Light",  loaded.Theme);
        Assert.Equal("en-US",  loaded.Language);
        Assert.Equal(10,       loaded.AutoActivateMinutes);
    }

    // ── LoadProfiles ──────────────────────────────────────────────────────────

    [Fact]
    public void LoadProfiles_MissingFile_ReturnsEmpty()
    {
        var profiles = SettingsService.LoadProfiles(At("profiles.json"));
        Assert.Empty(profiles);
    }

    [Fact]
    public void LoadProfiles_InvalidJson_ReturnsEmpty()
    {
        File.WriteAllText(At("profiles.json"), "not json");

        var profiles = SettingsService.LoadProfiles(At("profiles.json"));
        Assert.Empty(profiles);
    }

    [Fact]
    public void LoadProfiles_ValidJson_ReturnsProfiles()
    {
        File.WriteAllText(At("profiles.json"),
            """[{"Id":"abc","Name":"Work"},{"Id":"def","Name":"Personal"}]""");

        var profiles = SettingsService.LoadProfiles(At("profiles.json"));

        Assert.Equal(2,          profiles.Count);
        Assert.Equal("Work",     profiles[0].Name);
        Assert.Equal("Personal", profiles[1].Name);
    }

    // ── SaveProfiles / Round-trip ─────────────────────────────────────────────

    [Fact]
    public void SaveAndLoadProfiles_RoundTrip()
    {
        var path = At("profiles.json");
        var original = new List<ProfileConfig>
        {
            new() { Id = "aaa", Name = "Work"     },
            new() { Id = "bbb", Name = "Personal" },
        };

        SettingsService.SaveProfiles(path, original);
        var loaded = SettingsService.LoadProfiles(path);

        Assert.Equal(2,          loaded.Count);
        Assert.Equal("aaa",      loaded[0].Id);
        Assert.Equal("Work",     loaded[0].Name);
        Assert.Equal("Personal", loaded[1].Name);
    }

    // ── CleanupOrphanedProfileFolders ─────────────────────────────────────────

    [Fact]
    public void Cleanup_RemovesOrphanedFolders()
    {
        var dir = Path.Combine(_tempDir, "profiles");
        Directory.CreateDirectory(Path.Combine(dir, "known1"));
        Directory.CreateDirectory(Path.Combine(dir, "known2"));
        Directory.CreateDirectory(Path.Combine(dir, "orphan"));

        SettingsService.CleanupOrphanedProfileFolders(dir, ["known1", "known2"]);

        Assert.True (Directory.Exists(Path.Combine(dir, "known1")));
        Assert.True (Directory.Exists(Path.Combine(dir, "known2")));
        Assert.False(Directory.Exists(Path.Combine(dir, "orphan")));
    }

    [Fact]
    public void Cleanup_KeepsAllFolders_WhenAllAreKnown()
    {
        var dir = Path.Combine(_tempDir, "profiles");
        Directory.CreateDirectory(Path.Combine(dir, "id1"));
        Directory.CreateDirectory(Path.Combine(dir, "id2"));

        SettingsService.CleanupOrphanedProfileFolders(dir, ["id1", "id2"]);

        Assert.True(Directory.Exists(Path.Combine(dir, "id1")));
        Assert.True(Directory.Exists(Path.Combine(dir, "id2")));
    }

    [Fact]
    public void Cleanup_NonExistentDirectory_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            SettingsService.CleanupOrphanedProfileFolders(
                Path.Combine(_tempDir, "nonexistent"), []));
        Assert.Null(ex);
    }
}
