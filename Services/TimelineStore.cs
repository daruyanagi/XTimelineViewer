using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XTimelineViewer.Models;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// タイムライン一覧（timelines.json）の永続化を担当するサービス（#368）。
    /// UI 依存なし。パスは呼び出し側から受け取る（SettingsService と同じ流儀）。
    ///
    /// このファイルが壊れると全ペインが黙って消えるため、#338 で入れた
    /// 3 つの不変条件をここで守る。詳細は SaveAsync のコメントを参照。
    /// </summary>
    internal static class TimelineStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        // 保存の同時実行を防ぐ。追加・並べ替えなどから fire-and-forget で呼ばれるため、
        // I/O が重なって timelines.json が競合するのを避ける（#338）。
        private static readonly SemaphoreSlim SaveLock = new(1, 1);

        /// <summary>
        /// タイムライン一覧を読み込む。ファイルが無い・壊れている場合は空を返す。
        /// </summary>
        public static List<TimelineConfig> Load(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<List<TimelineConfig>>(json) ?? [];
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// タイムライン一覧を保存する。失敗したら例外を投げるので、
        /// fire-and-forget で呼ぶ側が握って記録すること。
        /// </summary>
        /// <remarks>
        /// 守るべき不変条件が 3 つある（#338）。
        ///
        /// 1. <b>シリアライズを await の前に同期実行する</b>。
        ///    呼び出し元は UI スレッドから fire-and-forget で呼ぶため、await を挟むと
        ///    その隙に configs が変更されうる。最初の await より前にスナップショットを取る。
        /// 2. <b>SemaphoreSlim で直列化する</b>。呼び出しが重なると I/O が競合する。
        /// 3. <b>tmp に書いてから置換する</b>。WriteAllTextAsync は truncate してから
        ///    書くので、途中で落ちると timelines.json が壊れる。Load は例外を握りつぶす
        ///    ので、壊れると全ペインが黙って消えてしまう。
        /// </remarks>
        public static async Task SaveAsync(string filePath, IReadOnlyList<TimelineConfig> configs)
        {
            // 【不変条件 1】await より前に同期でスナップショットを取る。ここを
            // await の後ろへ動かすと、保存内容が呼び出し時点とずれる。
            var json = JsonSerializer.Serialize(configs, JsonOptions);

            // 【不変条件 2】
            await SaveLock.WaitAsync();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                // 【不変条件 3】
                var tmp = filePath + ".tmp";
                await File.WriteAllTextAsync(tmp, json);
                File.Move(tmp, filePath, overwrite: true);
            }
            finally
            {
                SaveLock.Release();
            }
        }
    }
}
