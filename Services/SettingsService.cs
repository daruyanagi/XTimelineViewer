using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using XTimelineViewer.Models;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// アプリ設定・プロファイルの JSON 永続化を担当するサービス。
    /// UI 依存なし。MainWindow から状態を受け取り、ファイル I/O のみを行う。
    /// </summary>
    internal static class SettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        // ── App settings ──────────────────────────────────────────────────────

        public static AppSettings LoadSettings(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new();
            }
            catch
            {
                return new();
            }
        }

        public static void SaveSettings(string filePath, AppSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(settings, JsonOptions));
        }

        // ── Profiles ──────────────────────────────────────────────────────────

        public static List<ProfileConfig> LoadProfiles(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<List<ProfileConfig>>(json) ?? [];
            }
            catch
            {
                return [];
            }
        }

        public static void SaveProfiles(string filePath, List<ProfileConfig> profiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(profiles, JsonOptions));
        }

        /// <summary>
        /// <paramref name="profilesDir"/> 内のフォルダのうち
        /// <paramref name="knownIds"/> に含まれないものを孤立フォルダとして削除する。
        /// </summary>
        public static void CleanupOrphanedProfileFolders(string profilesDir, IEnumerable<string> knownIds)
        {
            try
            {
                if (!Directory.Exists(profilesDir)) return;
                var knownSet = new HashSet<string>(knownIds);
                foreach (var folder in Directory.GetDirectories(profilesDir))
                {
                    var name = Path.GetFileName(folder);
                    if (!knownSet.Contains(name))
                    {
                        try
                        {
                            Directory.Delete(folder, recursive: true);
                            Debug.WriteLine($"[Profile] Cleaned up orphaned folder: {name}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Profile] Failed to clean up orphaned folder: {name} ({ex.Message})");
                        }
                    }
                }
            }
            catch { /* 走査自体の失敗は無視。掃除できなくても動作に支障は無い */ }
        }
    }
}
