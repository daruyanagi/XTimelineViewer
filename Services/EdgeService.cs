using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// Microsoft Edge の検出・プロファイル列挙・プロファイル指定起動を担当するサービス。
    /// UI 依存なし。
    /// </summary>
    internal static class EdgeService
    {
        /// <summary>
        /// Edge Stable チャネルの msedge.exe パスを返す。見つからなければ null。
        /// </summary>
        public static string? FindEdgePath()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Edge", "Application", "msedge.exe"),
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    Debug.WriteLine($"[Edge] Found: {path}");
                    return path;
                }
            }

            Debug.WriteLine("[Edge] msedge.exe not found");
            return null;
        }

        /// <summary>
        /// Edge Stable チャネルの User Data ディレクトリパスを返す。
        /// </summary>
        internal static string GetUserDataPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "User Data");
        }

        /// <summary>
        /// Edge のプロファイル一覧を列挙する。
        /// Local State ファイルの profile.info_cache から取得する。
        /// </summary>
        public static List<EdgeProfile> EnumerateProfiles()
        {
            var result = new List<EdgeProfile>();
            try
            {
                var localStatePath = Path.Combine(GetUserDataPath(), "Local State");
                if (!File.Exists(localStatePath)) return result;

                var json = File.ReadAllText(localStatePath);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("profile", out var profileRoot)) return result;
                if (!profileRoot.TryGetProperty("info_cache", out var infoCache)) return result;

                foreach (var entry in infoCache.EnumerateObject())
                {
                    var dir = entry.Name; // "Default", "Profile 1" など
                    var displayName = dir;
                    if (entry.Value.TryGetProperty("name", out var nameProp) &&
                        nameProp.GetString() is { Length: > 0 } name)
                    {
                        displayName = name;
                    }
                    result.Add(new EdgeProfile(dir, displayName));
                }

                Debug.WriteLine($"[Edge] Enumerated {result.Count} profile(s)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Edge] EnumerateProfiles failed: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// 指定プロファイルで Edge を使って URI を開く。
        /// </summary>
        public static void LaunchInProfile(string edgePath, string profileDirectory, Uri uri)
        {
            var args = $"--profile-directory=\"{profileDirectory}\" \"{uri.AbsoluteUri}\"";
            Debug.WriteLine($"[Edge] Launch: {edgePath} {args}");
            Process.Start(new ProcessStartInfo
            {
                FileName = edgePath,
                Arguments = args,
                UseShellExecute = false,
            });
        }
    }

    /// <summary>
    /// Edge プロファイル情報。
    /// </summary>
    internal record EdgeProfile(string Directory, string DisplayName);
}
