// xtv.exe — XTimelineViewer 用の極小ネイティブランチャー（#264）
//
// 目的: winget portable（ZIP）は PortableCommandAlias を symlink で作るが、
// .NET self-contained の apphost は symlink の場所基準で DLL を探すため、本体
// XTimelineViewer.exe を symlink 経由で直接起動すると DLL 解決に失敗する。
// 依存 DLL を持たないこの小さなランチャーを噛ませ、自分の実体パスを symlink 越しに
// 解決して、隣にある XTimelineViewer.exe を正しい作業ディレクトリで起動する。
//
// ビルド（一度だけ・成果物 xtv.exe をリポジトリにコミットする。CI では再ビルドしない）:
//   "%ProgramFiles%\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
//   rc /nologo /fo xtv.res xtv.rc
//   cl /nologo /utf-8 /O1 /MT /EHsc /DUNICODE /D_UNICODE xtv.cpp xtv.res /Fe:xtv.exe /link /SUBSYSTEM:WINDOWS Shell32.lib
//   （rc で xtv.rc のアイコン(#270)を埋め込む。/MT で CRT を静的リンク＝VC ランタイム DLL 非依存。
//    /SUBSYSTEM:WINDOWS でコンソール窓を出さない）

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <string>

static std::wstring DirOf(const std::wstring& path)
{
    const size_t p = path.find_last_of(L"\\/");
    return (p == std::wstring::npos) ? std::wstring(L".") : path.substr(0, p);
}

// symlink を解決して実体のフルパスを返す。失敗時は入力をそのまま返す。
static std::wstring ResolveFinalPath(const std::wstring& path)
{
    HANDLE h = CreateFileW(path.c_str(), 0,
                           FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                           nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, nullptr);
    if (h == INVALID_HANDLE_VALUE) return path;

    wchar_t buf[32768];
    const DWORD n = GetFinalPathNameByHandleW(h, buf, (DWORD)(sizeof(buf) / sizeof(buf[0])),
                                              FILE_NAME_NORMALIZED);
    CloseHandle(h);
    if (n == 0 || n >= (DWORD)(sizeof(buf) / sizeof(buf[0]))) return path;

    std::wstring r(buf, n);
    // \\?\ プレフィックスを除去（UNC は \\?\UNC\ → \\ に戻す）
    if (r.rfind(L"\\\\?\\UNC\\", 0) == 0)      r = L"\\\\" + r.substr(8);
    else if (r.rfind(L"\\\\?\\", 0) == 0)      r = r.substr(4);
    return r;
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    wchar_t self[MAX_PATH];
    const DWORD n = GetModuleFileNameW(nullptr, self, MAX_PATH);
    if (n == 0) return 1;

    const std::wstring real   = ResolveFinalPath(std::wstring(self, n)); // symlink 越しに実体化
    const std::wstring dir    = DirOf(real);
    const std::wstring target = dir + L"\\XTimelineViewer.exe";

    // 自分に渡された引数（プログラム名トークンの後ろ全部）を本体へ転送する。
    std::wstring tail;
    {
        const std::wstring full(GetCommandLineW());
        size_t i = 0;
        if (!full.empty() && full[0] == L'"')
        {
            i = full.find(L'"', 1);
            i = (i == std::wstring::npos) ? full.size() : i + 1;
        }
        else
        {
            i = full.find(L' ');
            i = (i == std::wstring::npos) ? full.size() : i;
        }
        tail = (i < full.size()) ? full.substr(i) : std::wstring(); // 先頭の空白は維持
    }

    std::wstring cmd = L"\"" + target + L"\"" + tail;

    STARTUPINFOW        si{};
    si.cb = sizeof(si);
    PROCESS_INFORMATION pi{};

    const BOOL ok = CreateProcessW(target.c_str(), &cmd[0], nullptr, nullptr, FALSE,
                                   0, nullptr, dir.c_str(), &si, &pi);
    if (!ok)
    {
        // フォールバック（万一 CreateProcess が失敗した場合）
        ShellExecuteW(nullptr, L"open", target.c_str(), tail.c_str(), dir.c_str(), SW_SHOWNORMAL);
        return 0;
    }

    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return 0;
}
