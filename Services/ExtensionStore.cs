using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// 拡張機能の置き場（#396）。
    ///
    /// 以前はインストール先の中（<c>AppContext.BaseDirectory/extensions</c>）に置いていた。
    /// しかし更新はインストール先ごと置き換えるため、<b>利用者が入れた拡張機能が消えていた</b>。
    /// winget 版は `winget upgrade` がパッケージフォルダーを置き換えるので以前から、
    /// ZIP 版は #328 の自前更新が入って同じことが起きるようになった。
    ///
    /// 設定やプロファイルと同じくアプリ本体と独立した場所へ移す。
    /// UI に依存させない（テストプロジェクトからリンクして検証するため）。
    /// </summary>
    internal static class ExtensionStore
    {
        /// <summary>
        /// 旧い場所に残っている拡張機能を新しい場所へ移す。移した数を返す。
        ///
        /// 同じ名前が新しい場所に既にある場合は<b>触らない</b>。
        /// 新しい方が利用者の意図した最新とみなす（旧い方で上書きすると巻き戻る）。
        /// </summary>
        /// <param name="copyOnly">
        /// 旧い場所を消さずに複製するだけにする。MSIX の WindowsApps 配下は
        /// 書き込めないため、パッケージ版ではこちらを使う。
        /// </param>
        internal static int Migrate(string oldDir, string newDir, bool copyOnly)
        {
            if (!Directory.Exists(oldDir)) return 0;

            Directory.CreateDirectory(newDir);
            var moved = 0;

            foreach (var src in Directory.GetDirectories(oldDir))
            {
                var name = Path.GetFileName(src);
                var dst  = Path.Combine(newDir, name);

                if (Directory.Exists(dst))
                {
                    AppLog.Debug($"ExtensionStore: {name} は移行先に既にあるので触らない");
                    continue;
                }

                try
                {
                    if (copyOnly) CopyDirectory(src, dst);
                    else          MoveDirectory(src, dst);
                    moved++;
                    AppLog.Debug($"ExtensionStore: {name} を移行した → {dst}");
                }
                catch (Exception ex)
                {
                    // 1 つ失敗しても残りは移す。移せなかったものは旧い場所に残るので、
                    // 次の起動でまた試みる（更新前ならまだ間に合う）。
                    AppLog.Error($"ExtensionStore.Migrate({name})", ex);
                }
            }

            return moved;
        }

        /// <summary>
        /// 移動する。別ボリュームだと <see cref="Directory.Move"/> は失敗するので、
        /// そのときは複製してから消す。
        ///
        /// 複製が終わってから消す順序を崩さないこと。先に消すと、途中で失敗したときに
        /// <b>拡張機能が両方から消える</b>。
        /// </summary>
        private static void MoveDirectory(string src, string dst)
        {
            try
            {
                Directory.Move(src, dst);
                return;
            }
            catch (IOException)
            {
                // 別ボリューム、または誰かが掴んでいる。複製に切り替える。
            }

            CopyDirectory(src, dst);

            try
            {
                Directory.Delete(src, recursive: true);
            }
            catch (Exception ex)
            {
                // 消せなくても移行自体は済んでいる。二重に読み込まれないよう、
                // 呼び出し側は新しい場所だけを見る。
                AppLog.Debug($"ExtensionStore: 旧い {Path.GetFileName(src)} を消せませんでした: {ex.Message}");
            }
        }

        internal static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);

            foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, dir)));

            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(dst, Path.GetRelativePath(src, file)), overwrite: true);
        }

        /// <summary>
        /// 拡張機能として読み込めるフォルダーの一覧。
        /// <c>manifest.json</c> を持たないものは WebView2 が受け付けないので外す。
        /// </summary>
        internal static IEnumerable<string> EnumerateExtensionDirs(string dir)
        {
            if (!Directory.Exists(dir)) return [];

            return Directory.GetDirectories(dir)
                            .Where(d => File.Exists(Path.Combine(d, "manifest.json")))
                            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
        }
    }
}
