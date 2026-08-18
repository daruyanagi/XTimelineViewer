using System;
using System.Runtime.InteropServices;

namespace XTimelineViewer
{
    /// <summary>アプリがどの経路で配布・インストールされたか。</summary>
    internal enum InstallChannel
    {
        /// <summary>MSIX パッケージ（Store 等）として実行中。</summary>
        Packaged,
        /// <summary>winget が管理するフォルダーから実行中。</summary>
        Winget,
        /// <summary>ZIP を展開したものなど、それ以外。</summary>
        Zip,
    }

    // GetCurrentPackageFullName を使って例外なしにパッケージ実行かどうか判定する。
    // try { ApplicationData.Current } catch パターンは毎回 InvalidOperationException を
    // スローしてデバッガーのノイズになるため、この P/Invoke 方式に統一する。
    internal static class PackageContext
    {
        private const int ERROR_APPMODEL_NO_PACKAGE = 15700;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFullName(ref int length, nint nameBuffer);

        internal static readonly bool IsPackaged;

        /// <summary>
        /// 配布経路（#327）。winget 版は %LocalAppData%\Microsoft\WinGet\Packages\ 配下に
        /// 展開されるため、実行中の exe のパスで判別できる。サポート時に「winget upgrade で
        /// 更新してください」と案内できるかの判断材料になる。
        /// </summary>
        internal static readonly InstallChannel Channel;

        static PackageContext()
        {
            int len = 0;
            IsPackaged = GetCurrentPackageFullName(ref len, 0) != ERROR_APPMODEL_NO_PACKAGE;
            Channel    = DetectChannel();
        }

        private static InstallChannel DetectChannel()
        {
            if (IsPackaged) return InstallChannel.Packaged;

            // Environment.ProcessPath は単一ファイル発行でも実 exe を指す。取得できなければ
            // AppContext.BaseDirectory（末尾は区切り文字）で代用する。
            var path = Environment.ProcessPath ?? AppContext.BaseDirectory;
            return path.Contains(@"\WinGet\Packages\", StringComparison.OrdinalIgnoreCase)
                ? InstallChannel.Winget
                : InstallChannel.Zip;
        }
    }
}
