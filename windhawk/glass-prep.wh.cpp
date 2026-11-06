// ==WindhawkMod==
// @id              glass-prep
// @name            Glass Prep
// @description     For GlassFollow: plain transparency (no Mica/blur) and hidden from screen capture
// @version         0.6
// @author          local
// @include         zen.exe
// @include         explorer.exe
// @include         discord.exe
// @include         steamwebhelper.exe
// @include         Flow.Launcher.exe
// @include         WindowsTerminal.exe
// @include         conhost.exe
// @include         OpenConsole.exe
// @compilerOptions -ldwmapi -luser32
// ==/WindhawkMod==

// ==WindhawkModSettings==
/*
- excludeFromCapture: true
  $name: Hide windows from screen capture
  $description: Needed so the glass under the window does not refract the window itself. Side effect, the window also disappears from screenshots, recordings and screen sharing.
- method: accent
  $name: Transparency method
  $description: Try them in this order if a window stays black or grey.
  $options:
  - accent: 1. Transparent accent (SetWindowCompositionAttribute)
  - accentFrame: 2. Transparent accent + frame extended into client area
  - backdropNone: 3. Only "no system backdrop" + frame extended
*/
// ==/WindhawkModSettings==

#include <windows.h>
#include <dwmapi.h>
#include <set>

static bool g_exclude = true;
static int g_method = 0; // 0 accent, 1 accent + frame, 2 backdrop none + frame
static bool g_isExplorer = false;
static int g_minH = 200; // Flow Launcher's search bar is a thin window
static volatile bool g_run = false;
static HANDLE g_thread = nullptr;

using DwmSetWindowAttribute_t = HRESULT(WINAPI*)(HWND, DWORD, LPCVOID, DWORD);
static DwmSetWindowAttribute_t DwmSetWindowAttribute_Original = nullptr;

struct ACCENT_POLICY { int AccentState; int AccentFlags; DWORD GradientColor; int AnimationId; };
struct WINCOMPATTRDATA { int Attribute; PVOID Data; ULONG DataSize; };
using SetWindowCompositionAttribute_t = BOOL(WINAPI*)(HWND, WINCOMPATTRDATA*);
static SetWindowCompositionAttribute_t SetWCA = nullptr;

// In explorer.exe only File Explorer windows are touched: never the desktop/wallpaper, taskbar or shell popups
// (excluding the wallpaper window from capture would make the glass refract black).
static bool IsDialog(HWND h)
{
    wchar_t cls[64] = {};
    GetClassNameW(h, cls, 64);
    return wcscmp(cls, L"#32770") == 0; // standard Windows dialog (delete confirmation, properties, ...)
}

static bool ShouldHandle(HWND h)
{
    if (!g_isExplorer) return true;
    wchar_t cls[64] = {};
    GetClassNameW(h, cls, 64);
    return wcscmp(cls, L"CabinetWClass") == 0 || wcscmp(cls, L"#32770") == 0;
}

static HRESULT CallDwm(HWND h, DWORD attr, LPCVOID value, DWORD size)
{
    return DwmSetWindowAttribute_Original ? DwmSetWindowAttribute_Original(h, attr, value, size)
                                          : DwmSetWindowAttribute(h, attr, value, size);
}

// The app (or another mod) asks for Mica/Acrylic: answer "no backdrop" instead.
static HRESULT WINAPI DwmSetWindowAttribute_Hook(HWND hwnd, DWORD attr, LPCVOID value, DWORD size)
{
    if (attr == 38 && ShouldHandle(hwnd)) // DWMWA_SYSTEMBACKDROP_TYPE
    {
        DWORD none = 1; // DWMSBT_NONE
        return CallDwm(hwnd, attr, &none, sizeof(none));
    }
    return CallDwm(hwnd, attr, value, size);
}

static void ApplyAccent(HWND h)
{
    if (!SetWCA) return;
    ACCENT_POLICY ap = {};
    ap.AccentState = 2;      // ACCENT_ENABLE_TRANSPARENTGRADIENT
    ap.AccentFlags = 2;
    ap.GradientColor = 0x00000000; // fully transparent tint (AABBGGRR)
    WINCOMPATTRDATA d = {19, &ap, sizeof(ap)}; // WCA_ACCENT_POLICY
    SetWCA(h, &d);
}

static int g_pass = 0;

static BOOL CALLBACK EnumProc(HWND h, LPARAM lp)
{
    DWORD pid = 0;
    GetWindowThreadProcessId(h, &pid);
    if (pid != GetCurrentProcessId()) return TRUE;
    if (!IsWindowVisible(h)) return TRUE;
    bool dlg = IsDialog(h);
    if (GetWindow(h, GW_OWNER) && !dlg) return TRUE; // owned windows are skipped, except dialogs
    RECT r;
    if (!GetWindowRect(h, &r) || r.right - r.left < (dlg ? 150 : 300) || r.bottom - r.top < (dlg ? 80 : g_minH)) return TRUE; // main windows and dialogs only
    if (!ShouldHandle(h)) return TRUE;

    auto& done = *reinterpret_cast<std::set<HWND>*>(lp);

    DWORD aff = 0;
    GetWindowDisplayAffinity(h, &aff);
    DWORD want = g_exclude ? 0x11 : 0; // WDA_EXCLUDEFROMCAPTURE / WDA_NONE
    if (aff != want && (g_exclude || done.count(h))) SetWindowDisplayAffinity(h, want);

    bool first = !done.count(h);
    if (first)
    {
        DWORD none = 1;
        CallDwm(h, 38, &none, sizeof(none));
        if (g_method != 0)
        {
            MARGINS m = {-1, -1, -1, -1};
            DwmExtendFrameIntoClientArea(h, &m);
        }
    }
    if (g_method != 2 && (first || g_pass % 10 == 0)) ApplyAccent(h); // re-apply now and then, the app may reset it
    done.insert(h);
    return TRUE;
}

static DWORD WINAPI Worker(LPVOID)
{
    std::set<HWND> done;
    while (g_run)
    {
        EnumWindows(EnumProc, reinterpret_cast<LPARAM>(&done));
        g_pass++;
        Sleep(300);
    }
    for (HWND h : done)
        if (IsWindow(h)) SetWindowDisplayAffinity(h, 0);
    return 0;
}

static void LoadSettings()
{
    g_exclude = Wh_GetIntSetting(L"excludeFromCapture") != 0;
    PCWSTR m = Wh_GetStringSetting(L"method");
    g_method = 0;
    if (m)
    {
        if (wcscmp(m, L"accentFrame") == 0) g_method = 1;
        else if (wcscmp(m, L"backdropNone") == 0) g_method = 2;
        Wh_FreeStringSetting(m);
    }
}

BOOL Wh_ModInit()
{
    LoadSettings();
    wchar_t path[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    const wchar_t* name = wcsrchr(path, L'\\');
    g_isExplorer = name && _wcsicmp(name + 1, L"explorer.exe") == 0;
    if (name && _wcsicmp(name + 1, L"Flow.Launcher.exe") == 0) g_minH = 30;

    HMODULE dwm = LoadLibraryW(L"dwmapi.dll");
    void* p = dwm ? reinterpret_cast<void*>(GetProcAddress(dwm, "DwmSetWindowAttribute")) : nullptr;
    if (p) Wh_SetFunctionHook(p, reinterpret_cast<void*>(DwmSetWindowAttribute_Hook), reinterpret_cast<void**>(&DwmSetWindowAttribute_Original));
    HMODULE u32 = LoadLibraryW(L"user32.dll");
    SetWCA = u32 ? reinterpret_cast<SetWindowCompositionAttribute_t>(GetProcAddress(u32, "SetWindowCompositionAttribute")) : nullptr;
    g_run = true;
    g_thread = CreateThread(nullptr, 0, Worker, nullptr, 0, nullptr);
    return TRUE;
}

void Wh_ModSettingsChanged()
{
    LoadSettings();
}

void Wh_ModUninit()
{
    g_run = false;
    if (g_thread)
    {
        WaitForSingleObject(g_thread, 3000);
        CloseHandle(g_thread);
    }
}
