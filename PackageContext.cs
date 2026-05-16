using System.Runtime.InteropServices;

namespace XTimelineViewer
{
    // GetCurrentPackageFullName を使って例外なしにパッケージ実行かどうか判定する。
    // try { ApplicationData.Current } catch パターンは毎回 InvalidOperationException を
    // スローしてデバッガーのノイズになるため、この P/Invoke 方式に統一する。
    internal static class PackageContext
    {
        private const int ERROR_APPMODEL_NO_PACKAGE = 15700;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFullName(ref int length, nint nameBuffer);

        internal static readonly bool IsPackaged;

        static PackageContext()
        {
            int len = 0;
            IsPackaged = GetCurrentPackageFullName(ref len, 0) != ERROR_APPMODEL_NO_PACKAGE;
        }
    }
}
