// saapi64.cpp — Minimal Diablo IV TTS capture shim for D4Scanner.
//
// Diablo IV's "Use 3rd Party Screen Reader" accessibility option routes UI text
// through the Tolk library, which loads a "System Access" screen reader named
// saapi64.dll and calls four functions on it. This DLL pretends to be that
// screen reader: instead of speaking, every line D4 tries to voice is appended
// (UTF-8 + newline) to a log file that the Python parser tails.
//
// It reads NO game memory and injects NO code into Diablo IV — it only receives
// text the game voluntarily hands to the OS accessibility layer.
//
// Build x64. Output name MUST be exactly saapi64.dll, and it MUST be
// Authenticode-signed or Diablo IV refuses to load it. See build-and-install.ps1.
//
// Log path: %D4TTS_LOG% if set, else %LOCALAPPDATA%\d4scanner\d4_tts.log

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>

static std::wstring ResolveLogPath()
{
    wchar_t buf[MAX_PATH];
    DWORD n = GetEnvironmentVariableW(L"D4TTS_LOG", buf, MAX_PATH);
    if (n > 0 && n < MAX_PATH)
        return std::wstring(buf, n);

    n = GetEnvironmentVariableW(L"LOCALAPPDATA", buf, MAX_PATH);
    if (n > 0 && n < MAX_PATH)
    {
        std::wstring dir(buf, n);
        dir += L"\\d4scanner";
        CreateDirectoryW(dir.c_str(), nullptr); // ok if it already exists
        return dir + L"\\d4_tts.log";
    }
    return L"d4_tts.log"; // last resort: game working directory
}

static void AppendLine(const wchar_t* text)
{
    if (!text || !*text)
        return;

    int bytes = WideCharToMultiByte(CP_UTF8, 0, text, -1, nullptr, 0, nullptr, nullptr);
    if (bytes <= 1)
        return;
    std::string utf8(static_cast<size_t>(bytes) - 1, '\0'); // drop trailing NUL
    WideCharToMultiByte(CP_UTF8, 0, text, -1, &utf8[0], bytes, nullptr, nullptr);
    utf8.push_back('\n');

    static const std::wstring path = ResolveLogPath();
    HANDLE h = CreateFileW(path.c_str(), FILE_APPEND_DATA,
                           FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
                           OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE)
        return;
    DWORD wrote = 0;
    WriteFile(h, utf8.data(), static_cast<DWORD>(utf8.size()), &wrote, nullptr);
    CloseHandle(h);
}

// The four System Access entry points Tolk resolves via GetProcAddress.
// On x64 there is one calling convention, so these export undecorated:
// SA_SayW, SA_BrlShowTextW, SA_StopAudio, SA_IsRunning.
extern "C" {

__declspec(dllexport) bool SA_SayW(const wchar_t* text)         { AppendLine(text); return true; }
__declspec(dllexport) bool SA_BrlShowTextW(const wchar_t* text) { (void)text;       return true; }
__declspec(dllexport) bool SA_StopAudio()                       { return true; }
__declspec(dllexport) bool SA_IsRunning()                       { return true; } // MUST be true or D4 won't "speak"

} // extern "C"

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(hModule);
        AppendLine(L"=== d4scanner tts shim attached ===");
    }
    return TRUE;
}
