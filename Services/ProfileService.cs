using System;
using System.IO;

namespace XTimelineViewer.Services
{
    /// <summary>
    /// プロファイル関連のパス算出など、UI 非依存のヘルパー。
    /// </summary>
    internal static class ProfileService
    {
        /// <summary>
        /// 名前付きプロファイルの WebView2 ユーザーデータ保存先ディレクトリ。
        /// MSIX パッケージ環境では LocalFolder 配下、unpackaged では LocalApplicationData 配下。
        /// （旧 AddProfileWindow / MainWindow の重複実装を共通化 — #157）
        /// </summary>
        public static string GetProfilesDataDir()
        {
            if (PackageContext.IsPackaged)
                return Path.Combine(
                    Windows.Storage.ApplicationData.Current.LocalFolder.Path, "profiles");
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XTimelineViewer", "profiles");
        }
    }
}
