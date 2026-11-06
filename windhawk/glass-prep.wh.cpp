// ==WindhawkMod==
// @id              glass-prep
// @name            Glass Prep
// @description     For GlassFollow: plain transparency (no Mica/blur), hidden from capture while GlassFollow runs (except during screenshots), per-app settings
// @version         0.7
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
  $name: Hide windows from screen capture (default for all apps)
  $description: Needed so the glass does not refract the window itself. Windows are hidden only while GlassFollow is running, and become visible during Win+Shift+S / PrtScn screenshots. Screen recording and sharing do not see them.
- method: accent
  $name: Transparency method (default for all apps)
  $description: Try them in this order if a window stays black or grey.
  $options:
  - accent: 1. Transparent accent (SetWindowCompositionAttribute)
  - accentFrame: 2. Transparent accent + frame extended into client area
  - backdropNone: 3. Only "no system backdrop" + frame extended
- apps:
  - - process: ""
      $name: Process name
      $description: For example zen.exe. The process must also be covered by this mod's include list.
    - enabled: true
      $name: Enabled
    - excludeFromCapture: true
      $name: Hide from screen capture
    - method: accent
      $name: Transparency method
      $options:
      - accent: 1. Transparent accent (SetWindowCompositionAttribute)
      - accentFrame: 2. Transparent accent + frame extended into client area
      - backdropNone: 3. Only "no system backdrop" + frame extended
    - classes: ""
      $name: Window classes
      $description: Comma-separated window classes to handle, a trailing * means prefix. Empty = every large window of the process (in explorer.exe, File Explorer windows and standard dialogs only).
  $name: Per-app settings
  $description: Overrides the defaults above for one process. Apps without an entry use the defaults.
*/
// ==/WindhawkModSettings==

#include <windows.h>
#include <dwmapi.h>
#include <map>
#include <string>
#include <vector>

struct AppConfig
{
    bool enabled = true;
    bool exclude = true;
    int method = 0; // 0 accent, 1 accent + frame, 2 backdrop none + frame
    std::vector<std::wstring> classes;
};

static AppConfig g_cfg;
static int g_epoch = 0;
static CRITICAL_SECTION g_lock;
static bool g_isExplorer = false;
static int g_minH = 200; // Flow Launcher's search bar is a thin window
static wchar_t g_exeName[MAX_PATH] = {};
static volatile bool g_run = false;
static HANDLE g_thread = nullptr;

using DwmSetWindowAttribute_t = HRESULT(WINAPI*)(HWND, DWORD, LPCVOID, DWORD);
static DwmSetWindowAttribute_t DwmSetWindowAttribute_Original = nullptr;

struct ACCENT_POLICY { int AccentState; int AccentFlags; DWORD GradientColor; int AnimationId; };
struct WINCOMPATTRDATA { int Attribute; PVOID Data; ULONG DataSize; };
using SetWindowCompositionAttribute_t = BOOL(WINAPI*)(HWND, WINCOMPATTRDATA*);
static SetWindowCompositionAttribute_t SetWCA = nullptr;

static AppConfig GetCfg(int* epoch = nullptr)
{
    EnterCriticalSection(&g_lock);
    AppConfig c = g_cfg;
    if (epoch) *epoch = g_epoch;
    LeaveCriticalSection(&g_lock);
    return c;
}

static int ParseMethod(PCWSTR m)
{
    if (!m) return 0;
    if (wcscmp(m, L"accentFrame") == 0) return 1;
    if (wcscmp(m, L"backdropNone") == 0) return 2;
    return 0;
}

static std::vector<std::wstring> SplitClasses(PCWSTR s)
{
    std::vector<std::wstring> out;
    if (!s) return out;
    std::wstring cur;
    auto flush = [&]() {
        size_t a = cur.find_first_not_of(L" \t");
        size_t b = cur.find_last_not_of(L" \t");
        if (a != std::wstring::npos) out.push_back(cur.substr(a, b - a + 1));
        cur.clear();
    };
    for (const wchar_t* p = s; *p; p++)
    {
        if (*p == L',' || *p == L';') flush();
        else cur.push_back(*p);
    }
    flush();
    return out;
}

static bool IsDialog(HWND h)
{
    wchar_t cls[64] = {};
    GetClassNameW(h, cls, 64);
    return wcscmp(cls, L"#32770") == 0; // standard Windows dialog (delete confirmation, properties, ...)
}

// Empty class list = every window of the process. In explorer.exe the default list keeps the desktop,
// wallpaper and taskbar untouched (excluding the wallpaper window from capture would make the glass refract black).
static bool ShouldHandle(HWND h, const AppConfig& c)
{
    if (c.classes.empty()) return true;
    wchar_t cls[128] = {};
    GetClassNameW(h, cls, 128);
    for (const auto& p : c.classes)
    {
        if (!p.empty() && p.back() == L'*')
        {
            if (_wcsnicmp(cls, p.c_str(), p.size() - 1) == 0) return true;
        }
        else if (_wcsicmp(cls, p.c_str()) == 0) return true;
    }
    return false;
}

static void LoadSettings()
{
    AppConfig c;
    c.exclude = Wh_GetIntSetting(L"excludeFromCapture") != 0;
    PCWSTR m = Wh_GetStringSetting(L"method");
    c.method = ParseMethod(m);
    Wh_FreeStringSetting(m);
    if (g_isExplorer) c.classes = {L"CabinetWClass", L"#32770"};

    for (int i = 0;; i++)
    {
        PCWSTR proc = Wh_GetStringSetting(L"apps[%d].process", i);
        bool has = proc && *proc;
        if (has && _wcsicmp(proc, g_exeName) == 0)
        {
            c.enabled = Wh_GetIntSetting(L"apps[%d].enabled", i) != 0;
            c.exclude = Wh_GetIntSetting(L"apps[%d].excludeFromCapture", i) != 0;
            PCWSTR mm = Wh_GetStringSetting(L"apps[%d].method", i);
            c.method = ParseMethod(mm);
            Wh_FreeStringSetting(mm);
            PCWSTR cl = Wh_GetStringSetting(L"apps[%d].classes", i);
            auto list = SplitClasses(cl);
            if (!list.empty()) c.classes = list;
            Wh_FreeStringSetting(cl);
        }
        Wh_FreeStringSetting(proc);
        if (!has) break;
    }

    EnterCriticalSection(&g_lock);
    g_cfg = c;
    g_epoch++;
    LeaveCriticalSection(&g_lock);
}

static HRESULT CallDwm(HWND h, DWORD attr, LPCVOID value, DWORD size)
{
    return DwmSetWindowAttribute_Original ? DwmSetWindowAttribute_Original(h, attr, value, size)
                                          : DwmSetWindowAttribute(h, attr, value, size);
}

// The app (or another mod) asks for Mica/Acrylic: answer "no backdrop" instead.
static HRESULT WINAPI DwmSetWindowAttribute_Hook(HWND hwnd, DWORD attr, LPCVOID value, DWORD size)
{
    if (attr == 38) // DWMWA_SYSTEMBACKDROP_TYPE
    {
        AppConfig c = GetCfg();
        if (c.enabled && ShouldHandle(hwnd, c))
        {
            DWORD none = 1; // DWMSBT_NONE
            return CallDwm(hwnd, attr, &none, sizeof(none));
        }
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

struct EnumCtx
{
    std::map<HWND, int>* done;
    bool wantExclude;
    int pass;
    int epoch;
    AppConfig cfg;
};

static BOOL CALLBACK EnumProc(HWND h, LPARAM lp)
{
    auto& x = *reinterpret_cast<EnumCtx*>(lp);
    if (!x.cfg.enabled) return TRUE;
    DWORD pid = 0;
    GetWindowThreadProcessId(h, &pid);
    if (pid != GetCurrentProcessId()) return TRUE;
    if (!IsWindowVisible(h)) return TRUE;

    bool explicitClasses = !x.cfg.classes.empty();
    bool dlg = IsDialog(h);
    if (GetWindow(h, GW_OWNER) && !dlg && !explicitClasses) return TRUE; // owned windows are skipped, except dialogs
    RECT r;
    if (!GetWindowRect(h, &r)) return TRUE;
    int minW = explicitClasses ? 100 : (dlg ? 150 : 300);
    int minH = explicitClasses ? 30 : (dlg ? 80 : g_minH);
    if (r.right - r.left < minW || r.bottom - r.top < minH) return TRUE;
    if (!ShouldHandle(h, x.cfg)) return TRUE;

    auto& done = *x.done;
    auto it = done.find(h);
    bool first = it == done.end() || it->second != x.epoch;

    DWORD aff = 0;
    GetWindowDisplayAffinity(h, &aff);
    DWORD want = x.wantExclude ? 0x11 : 0; // WDA_EXCLUDEFROMCAPTURE / WDA_NONE
    if (aff != want && (x.wantExclude || it != done.end())) SetWindowDisplayAffinity(h, want);

    if (first)
    {
        DWORD none = 1;
        CallDwm(h, 38, &none, sizeof(none));
        if (x.cfg.method != 0)
        {
            MARGINS m = {-1, -1, -1, -1};
            DwmExtendFrameIntoClientArea(h, &m);
        }
    }
    if (x.cfg.method != 2 && (first || x.pass % 10 == 0)) ApplyAccent(h); // re-apply now and then, the app may reset it
    done[h] = x.epoch;
    return TRUE;
}

static DWORD WINAPI Worker(LPVOID)
{
    std::map<HWND, int> done;
    HANDLE shotEvent = nullptr;
    bool lastShot = false, lastAlive = false;
    DWORD lastEnum = 0;
    int pass = 0;
    while (g_run)
    {
        // GlassFollow's invisible control window: the mod hides windows from capture only while it exists
        bool alive = FindWindowW(L"GlassFollow", L"GlassFollow.Control") != nullptr;
        if (!shotEvent) shotEvent = OpenEventW(SYNCHRONIZE, FALSE, L"Local\\GlassFollow.Screenshot");
        bool shot = shotEvent && WaitForSingleObject(shotEvent, 0) == WAIT_OBJECT_0;

        DWORD now = GetTickCount();
        if (shot != lastShot || alive != lastAlive || now - lastEnum >= 300)
        {
            lastShot = shot;
            lastAlive = alive;
            lastEnum = now;
            pass++;
            EnumCtx ctx;
            ctx.done = &done;
            ctx.cfg = GetCfg(&ctx.epoch);
            ctx.wantExclude = ctx.cfg.exclude && alive && !shot;
            ctx.pass = pass;
            EnumWindows(EnumProc, reinterpret_cast<LPARAM>(&ctx));
        }
        Sleep(20);
    }
    for (const auto& kv : done)
        if (IsWindow(kv.first)) SetWindowDisplayAffinity(kv.first, 0);
    if (shotEvent) CloseHandle(shotEvent);
    return 0;
}

BOOL Wh_ModInit()
{
    InitializeCriticalSection(&g_lock);
    wchar_t path[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    const wchar_t* name = wcsrchr(path, L'\\');
    wcsncpy_s(g_exeName, MAX_PATH, name ? name + 1 : path, _TRUNCATE);
    g_isExplorer = _wcsicmp(g_exeName, L"explorer.exe") == 0;
    if (_wcsicmp(g_exeName, L"Flow.Launcher.exe") == 0) g_minH = 30;
    LoadSettings();

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
    DeleteCriticalSection(&g_lock);
}
