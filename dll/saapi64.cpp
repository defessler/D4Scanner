// saapi64.cpp — Minimal Diablo IV TTS capture shim for D4Scanner.
//
// Diablo IV's "Use 3rd Party Screen Reader" accessibility option routes UI text
// through the Tolk library, which loads a "System Access" screen reader named
// saapi64.dll and calls four functions on it. This DLL pretends to be that
// screen reader: instead of speaking, every line D4 tries to voice is appended
// (UTF-8 + newline) to a log file that the C# app tails.
//
// Improvements over v1:
//   - ISO timestamp prefix on every line: "[2026-06-04T00:30:15Z]" lets the
//     parser detect session boundaries and item freshness.
//   - Deduplication: identical consecutive messages (D4 sometimes voices the
//     same text twice in rapid succession) are silently dropped.
//   - Session start/end markers include the timestamp.
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

// DLL version — bump this whenever the shim changes so the app knows to reinstall it.
// The app compares against this value at launch and reinstalls if the embedded version is newer.
#define SHIM_VERSION L"2"

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

// Returns the current UTC time as an ISO-8601 string: "2026-06-04T00:30:15Z"
static std::string IsoTimestamp()
{
    SYSTEMTIME st;
    GetSystemTime(&st);
    char buf[32];
    // Format: [YYYY-MM-DDTHH:MM:SSZ] — compact and parseable
    wsprintfA(buf, "[%04d-%02d-%02dT%02d:%02d:%02dZ]",
              (int)st.wYear, (int)st.wMonth, (int)st.wDay,
              (int)st.wHour, (int)st.wMinute, (int)st.wSecond);
    return std::string(buf);
}

static void AppendLine(const wchar_t* text)
{
    if (!text || !*text)
        return;

    // Deduplicate: skip identical consecutive messages
    // (D4 sometimes voices the same UI element twice in rapid succession)
    static std::wstring lastText;
    if (std::wstring(text) == lastText)
        return;
    lastText = text;

    int bytes = WideCharToMultiByte(CP_UTF8, 0, text, -1, nullptr, 0, nullptr, nullptr);
    if (bytes <= 1)
        return;
    std::string utf8(static_cast<size_t>(bytes) - 1, '\0'); // drop trailing NUL
    WideCharToMultiByte(CP_UTF8, 0, text, -1, &utf8[0], bytes, nullptr, nullptr);

    // Prefix with ISO timestamp so the parser can detect session boundaries
    // and callers can assess item freshness.  Format: [2026-06-04T00:30:15Z]TEXT\n
    std::string line = IsoTimestamp() + utf8 + "\n";

    static const std::wstring path = ResolveLogPath();
    HANDLE h = CreateFileW(path.c_str(), FILE_APPEND_DATA,
                           FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
                           OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE)
        return;
    DWORD wrote = 0;
    WriteFile(h, line.data(), static_cast<DWORD>(line.size()), &wrote, nullptr);
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
        AppendLine(L"=== d4scanner tts shim attached v" SHIM_VERSION " ===");
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        // Mark session end so the parser can use timestamps around this boundary
        // to identify items scanned in the current session vs. prior sessions.
        AppendLine(L"=== d4scanner tts shim detached ===");
    }
    return TRUE;
}
