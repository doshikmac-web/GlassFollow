// GlassFollow: liquid-glass refraction placed directly BEHIND chosen app windows; follows their position and size,
// across monitors (one capture session per monitor), for any number of windows/apps at once.
// Built on the MIT-licensed correct-liquid-glass demo (capture + shader + window-region approach).
//
// Usage: GlassFollow.exe [--corner 22] [--thick 36] [--fps 200] [--pad 16] <spec> [<spec> ...]
//        --hidden hides the console window (autostart); --no-shot ignores Win+Shift+S / PrtScn
//        --setup opens the interactive autostart setup (scripts\setup.ps1)
//        GlassFollow.exe --list            (prints visible windows: process / class / size / title, to build specs)
//   spec = process[:WindowClass[:TitleSubstring]]   (process without .exe)
//   process may be * (any process), class may end with * (prefix match)
//   e.g. GlassFollow.exe --thick 60 zen explorer:CabinetWClass discord steamwebhelper:SDL_app WindowsTerminal *:ConsoleWindowClass
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

var inv = CultureInfo.InvariantCulture;
float cornerArg = 22f, thickArg = 36f, padArg = 16f;
var style = GlassStyle.Correct; // default glass style; a spec can override it with a =uw / =correct suffix
// settings of the second style (uWidgets); the tint defaults follow uWidgets' dark / light theme
float uwRefraction = 100f, uwBlur = 0f, uwTint = -1f, uwRim = 0.54f, uwAngle = 87f, uwSheen = 0f, uwSpecular = 0f;
bool uwDark = true;
int fpsCap = 200, delaySec = 0;
bool listMode = false, hidden = false, noShot = false, setup = false;
int waitPid = 0, relaunchCount = 0;
var origArgs = args;
var specs = new List<Spec>();
for (int i = 0; i < args.Length; i++)
{
    string a = args[i];
    if (a == "--list") { listMode = true; continue; }
    if (a == "--hidden") { hidden = true; continue; } // no console window (for autostart)
    if (a == "--no-shot") { noShot = true; continue; } // do not watch for Win+Shift+S / PrtScn
    if (a == "--setup") { setup = true; continue; } // interactive autostart setup
    if (a.StartsWith("--") && i + 1 < args.Length)
    {
        string v = args[++i];
        switch (a)
        {
            case "--corner": cornerArg = float.Parse(v, inv); break;
            case "--thick": thickArg = float.Parse(v, inv); break;
            case "--fps": fpsCap = int.Parse(v, inv); break;
            case "--pad": padArg = float.Parse(v, inv); break;
            case "--style": style = Util.ParseStyle(v) ?? GlassStyle.Correct; break;
            case "--uw-refraction": uwRefraction = float.Parse(v, inv); break; // 0..100
            case "--uw-blur": uwBlur = float.Parse(v, inv); break;             // 0..100
            case "--uw-tint": uwTint = float.Parse(v, inv); break;             // 0..1, default 0.20 dark / 0.10 light
            case "--uw-theme": uwDark = !v.StartsWith("l", StringComparison.OrdinalIgnoreCase); break;
            case "--uw-rim": uwRim = float.Parse(v, inv); break;
            case "--uw-angle": uwAngle = float.Parse(v, inv); break;
            case "--uw-sheen": uwSheen = float.Parse(v, inv); break;
            case "--uw-specular": uwSpecular = float.Parse(v, inv); break;
            case "--delay": delaySec = int.Parse(v, inv); break;
            case "--wait-pid": waitPid = int.Parse(v, inv); break;      // internal: set by an automatic restart
            case "--relaunch": relaunchCount = int.Parse(v, inv); break; // internal: quick-restart counter
            default: Console.WriteLine($"unknown option {a}"); return;
        }
    }
    else
    {
        GlassStyle? specStyle = null;
        string body = a;
        int eq = a.LastIndexOf('=');
        if (eq > 0 && Util.ParseStyle(a[(eq + 1)..]) is { } parsed) { specStyle = parsed; body = a[..eq]; } // zen=uw
        var p = body.Split(':', 3);
        specs.Add(new Spec(p[0].Replace(".exe", "", StringComparison.OrdinalIgnoreCase),
            p.Length > 1 && p[1].Length > 0 ? p[1] : null,
            p.Length > 2 && p[2].Length > 0 ? p[2] : null,
            specStyle));
    }
}

if (setup)
{
    var script = Path.Combine(AppContext.BaseDirectory, "scripts", "setup.ps1");
    if (!File.Exists(script)) { Console.WriteLine("scripts\\setup.ps1 not found next to the exe"); return; }
    using var sp = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"") { UseShellExecute = false });
    sp?.WaitForExit();
    return;
}

Native.SetProcessDpiAwarenessContext(-4);
if (hidden && !listMode) Native.FreeConsole();

// small log for recovery events (the console is gone when running hidden): %LOCALAPPDATA%\GlassFollow\glassfollow.log
string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassFollow", "glassfollow.log");
void Log(string text)
{
    try { Console.WriteLine(text); } catch { }
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        if (File.Exists(logPath) && new FileInfo(logPath).Length > 200_000) File.Delete(logPath);
        File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
    }
    catch { }
}

if (waitPid > 0) { try { using var wp = Process.GetProcessById(waitPid); wp.WaitForExit(8000); } catch { } }

// Start a fresh copy of this program and exit (used when the GPU device is lost or the program is about to crash).
void Relaunch(string reason)
{
    Log($"relaunching: {reason}");
    try
    {
        var keep = new List<string>();
        for (int i = 0; i < origArgs.Length; i++)
        {
            if (origArgs[i] is "--wait-pid" or "--relaunch") { i++; continue; }
            keep.Add(origArgs[i]);
        }
        if (!keep.Contains("--hidden")) keep.Insert(0, "--hidden");
        double up = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds;
        int k = up > 60 ? 0 : relaunchCount + 1; // a long healthy run resets the crash-loop counter
        if (k > 5) { Log("too many quick restarts, giving up"); Environment.Exit(1); }
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
        foreach (var x in keep) psi.ArgumentList.Add(x);
        psi.ArgumentList.Add("--wait-pid"); psi.ArgumentList.Add(Environment.ProcessId.ToString());
        psi.ArgumentList.Add("--relaunch"); psi.ArgumentList.Add(k.ToString());
        Process.Start(psi);
    }
    catch { Environment.Exit(1); }
    Environment.Exit(0);
}
if (!listMode) AppDomain.CurrentDomain.UnhandledException += (_, e) => { Log($"unhandled exception: {e.ExceptionObject}"); Relaunch("unhandled exception"); };

var names = new Dictionary<uint, string>();
string ProcName(uint pid)
{
    if (!names.TryGetValue(pid, out string? n))
    {
        try { n = Process.GetProcessById((int)pid).ProcessName; } catch { n = ""; }
        names[pid] = n;
    }
    return n;
}

if (listMode)
{
    if (delaySec > 0) { Console.WriteLine($"listing in {delaySec}s (open the window you need)..."); Thread.Sleep(delaySec * 1000); }
    var sbl = new StringBuilder(256);
    Native.EnumWindows((h, _) =>
    {
        if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
        if (Native.DwmGetWindowAttribute(h, 9, out Native.RECT r, Marshal.SizeOf<Native.RECT>()) != 0) return true;
        int w = r.right - r.left, hh = r.bottom - r.top;
        if (w < 150 || hh < 30) return true;
        Native.GetWindowThreadProcessId(h, out uint pid);
        sbl.Clear(); Native.GetClassNameW(h, sbl, 256); string cls = sbl.ToString();
        sbl.Clear(); Native.GetWindowTextW(h, sbl, 256); string title = sbl.ToString();
        Console.WriteLine($"{ProcName(pid),-22} {cls,-30} {w}x{hh,-6} {title}");
        return true;
    }, 0);
    return;
}
if (specs.Count == 0)
{
    Console.WriteLine("Usage: GlassFollow.exe [--corner 22] [--thick 36] [--fps 200] [--pad 16] <process[:Class[:Title]]> ...");
    Console.WriteLine("       GlassFollow.exe --list");
    return;
}

float s = Native.GetDpiForSystem() / 96f;

// --- monitors (virtual-screen coordinates, physical pixels) ---
List<Mon> EnumMonitors()
{
    var list = new List<Mon>();
    Native.EnumDisplayMonitors(0, 0, (hm, _, _, _) =>
    {
        var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfoW(hm, ref mi);
        list.Add(new Mon { Handle = hm, X = mi.rcMonitor.left, Y = mi.rcMonitor.top, W = mi.rcMonitor.right - mi.rcMonitor.left, H = mi.rcMonitor.bottom - mi.rcMonitor.top });
        return true;
    }, 0);
    return list;
}
var mons = EnumMonitors();

int pad = (int)(padArg * s), M = pad; // padding around the app rect; M = extra texture rows below the window
float corner = cornerArg * s, thick = thickArg * s;
var st = new State();
long processed = 0, skipped = 0;

// --- glass windows share one class ---
Native.WndProc proc = (h, m, w, l) =>
{
    if (m == 0x218 && (w == 0x7 || w == 0x12)) st.Request("resume from sleep"); // WM_POWERBROADCAST: PBT_APMRESUMESUSPEND / PBT_APMRESUMEAUTOMATIC
    else if (m == 0x7E) st.Request("display change");                          // WM_DISPLAYCHANGE
    return Native.DefWindowProcW(h, m, w, l);
};
var wc = new Native.WNDCLASSEXW { cbSize = Marshal.SizeOf<Native.WNDCLASSEXW>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc), lpszClassName = "GlassFollow" };
Native.RegisterClassExW(ref wc);
// invisible control window: the Windhawk mod looks for it to know that GlassFollow is running
Native.CreateWindowExW(0x80, "GlassFollow", "GlassFollow.Control", unchecked((int)0x80000000), 0, 0, 0, 0, 0, 0, 0, 0);

// --- d3d ---
D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out ID3D11Device? device, out ID3D11DeviceContext? ctx).CheckError();
using (var mt = device!.QueryInterface<ID3D11Multithread>()) mt.SetMultithreadProtected(true);
var dxgiDevice = device.QueryInterface<IDXGIDevice>();
dxgiDevice.GetAdapter(out IDXGIAdapter adapter).CheckError();
var factory = adapter.GetParent<IDXGIFactory2>();
foreach (var m in mons) // newest full frame of each monitor
    m.Tex = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)m.W, (uint)m.H, 1, 1, BindFlags.None));
var sampler = device.CreateSamplerState(SamplerDescription.LinearClamp);
var hlslPath = Path.Combine(AppContext.BaseDirectory, "liquid-glass.hlsl");
string hlsl = File.ReadAllText(hlslPath);
var vs = device.CreateVertexShader(Compiler.Compile(hlsl, "VS", "liquid-glass.hlsl", "vs_5_0").Span);
var ps = device.CreatePixelShader(Compiler.Compile(hlsl, "PS", "liquid-glass.hlsl", "ps_5_0").Span);

// second style (uWidgets): loaded only if some window asks for it; falls back to the default style when the file is missing
bool needUw = style == GlassStyle.UWidgets || specs.Any(x => x.Style == GlassStyle.UWidgets);
string uwPath = Path.Combine(AppContext.BaseDirectory, "liquid-glass-uwidgets.hlsl");
ID3D11VertexShader? vsUw = null;
ID3D11PixelShader? psUw = null, psBlur = null;
bool uwOk = false;
void LoadUw()
{
    string src = File.ReadAllText(uwPath);
    var nvs = device.CreateVertexShader(Compiler.Compile(src, "VSScreen", "liquid-glass-uwidgets.hlsl", "vs_5_0").Span);
    var nps = device.CreatePixelShader(Compiler.Compile(src, "PSGlass", "liquid-glass-uwidgets.hlsl", "ps_5_0").Span);
    var npb = device.CreatePixelShader(Compiler.Compile(src, "PSBlur", "liquid-glass-uwidgets.hlsl", "ps_5_0").Span);
    lock (ctx!) { vsUw?.Dispose(); psUw?.Dispose(); psBlur?.Dispose(); vsUw = nvs; psUw = nps; psBlur = npb; }
}
if (needUw)
{
    if (!File.Exists(uwPath)) Log("liquid-glass-uwidgets.hlsl not found next to the exe: the uWidgets style falls back to the default style");
    else
    {
        try { LoadUw(); uwOk = true; }
        catch (Exception e) { Log($"uWidgets shader failed to compile, using the default style: {e.Message}"); }
    }
}
FileSystemWatcher? watcherUw = null;
if (uwOk)
{
    watcherUw = new FileSystemWatcher(AppContext.BaseDirectory, "liquid-glass-uwidgets.hlsl") { NotifyFilter = NotifyFilters.LastWrite, EnableRaisingEvents = true };
    watcherUw.Changed += (_, _) =>
    {
        try
        {
            Thread.Sleep(100);
            LoadUw();
            foreach (var gl in st.Glasses) gl.NeedRender = true;
            Log("uWidgets shader reloaded");
        }
        catch (Exception e) { Log($"shader error: {e.Message}"); }
    };
}
GlassStyle Resolve(GlassStyle? requested)
{
    var x = requested ?? style;
    return x == GlassStyle.UWidgets && !uwOk ? GlassStyle.Correct : x;
}

// hot reload: edit liquid-glass.hlsl next to the exe
var watcher = new FileSystemWatcher(AppContext.BaseDirectory, "liquid-glass.hlsl") { NotifyFilter = NotifyFilters.LastWrite, EnableRaisingEvents = true };
watcher.Changed += (_, _) =>
{
    try
    {
        Thread.Sleep(100);
        var code = Compiler.Compile(File.ReadAllText(hlslPath), "PS", "liquid-glass.hlsl", "ps_5_0");
        lock (ctx!) { var old = ps; ps = device.CreatePixelShader(code.Span); old.Dispose(); }
        foreach (var gl in st.Glasses) gl.NeedRender = true;
        Console.WriteLine("shader reloaded");
    }
    catch (Exception e) { Console.WriteLine($"shader error: {e.Message}"); }
};

// One shape = the app window's rect, placed inside the (padded) glass window. The same shape is passed to all 6 slots
// of the demo's constant buffer so the unmodified demo shader works. R = (cornerRadius, "halfHeight" = bevel scale).
Cb MakeCb(Geo g)
{
    var rect = new Vector4(g.Pad, g.Pad, g.TW, g.TH);
    var rad = new Vector4(g.Radius, g.Thick, 0, 0);
    return new Cb
    {
        WinSize = new(g.W, g.H), TexSize = new(g.W, g.H + g.M),
        S0 = rect, S1 = rect, S2 = rect, S3 = rect, S4 = rect, S5 = rect,
        R0 = rad, R1 = rad, R2 = rad, R3 = rad, R4 = rad, R5 = rad,
        Flags = default,
    };
}

// uWidgets style: the same one shape, in uWidgets' own constant buffer layout (see liquid-glass-uwidgets.hlsl)
CbUw MakeCbUw(Geo g)
{
    float tintAmount = uwTint >= 0 ? uwTint : (uwDark ? 0.20f : 0.10f);
    var tint = uwDark ? new Vector4(28 / 255f, 28 / 255f, 32 / 255f, 1) : new Vector4(252 / 255f, 252 / 255f, 255 / 255f, 1);
    return new CbUw
    {
        Widget = new Vector4(g.Pad, g.Pad, g.TW, g.TH),
        Effect = new Vector4(1f, uwRefraction / 100f, tintAmount, 0),
        Tint = tint,
        Bounds = new Vector4(g.Radius, 0, 0, g.W - 1),
        Bounds2 = new Vector4(g.H + g.M - 1, g.W, g.H + g.M, 0),
        Rim = new Vector4(0.8f, 3.1f, 6.8f, uwRim),
        Light = new Vector4(2.38f, uwSheen, uwSpecular, 4f),
        Angle = new Vector4(uwAngle, 0, 0, 0),
    };
}

Glass CreateGlass(nint target, GlassStyle gstyle)
{
    // NOT topmost: it is stacked right under its target window
    var hw = Native.CreateWindowExW(0x80 | 0x08000000, "GlassFollow", "GlassFollow", unchecked((int)0x80000000), 0, 0, 64, 64, 0, 0, 0, 0);
    Native.SetWindowDisplayAffinity(hw, 0x11); // WDA_EXCLUDEFROMCAPTURE: the glass never refracts itself
    lock (ctx!)
    {
        var sw = factory.CreateSwapChainForHwnd(device, hw, new SwapChainDescription1
        {
            Width = 64, Height = 64, Format = Format.B8G8R8A8_UNorm, BufferCount = 2,
            BufferUsage = Usage.RenderTargetOutput, SampleDescription = new(1, 0),
            SwapEffect = SwapEffect.FlipDiscard, Scaling = Scaling.Stretch, AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
        });
        uint cbSize = gstyle == GlassStyle.UWidgets ? (uint)Marshal.SizeOf<CbUw>() : (uint)Marshal.SizeOf<Cb>();
        var cbuf = device.CreateBuffer(new BufferDescription(cbSize, BindFlags.ConstantBuffer));
        return new Glass { Hwnd = hw, Target = target, Swap = sw, Cb = cbuf, Style = gstyle };
    }
}

void DestroyGlass(Glass gl)
{
    lock (ctx!)
    {
        gl.Dead = true;
        gl.Srv?.Dispose(); gl.Strip?.Dispose(); gl.Cb.Dispose(); gl.Swap.Dispose();
        gl.BlurASrv?.Dispose(); gl.BlurBSrv?.Dispose(); gl.BlurA?.Dispose(); gl.BlurB?.Dispose(); gl.BlurCbH?.Dispose(); gl.BlurCbV?.Dispose();
    }
    Native.DestroyWindow(gl.Hwnd);
}

void ApplyRegion(nint hw, Geo g) // the window only exists inside the rounded rect (+1px for the AA edge)
{
    int d = g.Radius > 0 ? (int)(g.Radius * 2) + 2 : 0;
    nint rgn = Native.CreateRoundRectRgn(g.Pad - 1, g.Pad - 1, g.Pad + g.TW + 2, g.Pad + g.TH + 2, d, d);
    Native.SetWindowRgn(hw, rgn, true); // the system owns rgn now
}

// uWidgets style: optional separable gaussian blur of the backdrop slice, then the glass pass
void DrawUw(Glass gl, Geo g, float blurPx)
{
    ctx!.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
    ctx.VSSetShader(vsUw);
    ctx.PSSetSampler(0, sampler);
    var source = gl.Srv!;
    if (blurPx >= 0.05f && gl.BlurA is not null && gl.BlurB is not null && gl.BlurASrv is not null && gl.BlurBSrv is not null)
    {
        ctx.RSSetViewport(new Viewport(g.W, g.H + g.M));
        ctx.PSSetShader(psBlur);
        using (var rA = device.CreateRenderTargetView(gl.BlurA))
        {
            ctx.OMSetRenderTargets(rA);
            ctx.PSSetShaderResource(0, gl.Srv); ctx.PSSetConstantBuffer(1, gl.BlurCbH!);
            ctx.Draw(3, 0);
        }
        ctx.PSSetShaderResource(0, (ID3D11ShaderResourceView?)null);
        using (var rB = device.CreateRenderTargetView(gl.BlurB))
        {
            ctx.OMSetRenderTargets(rB);
            ctx.PSSetShaderResource(0, gl.BlurASrv); ctx.PSSetConstantBuffer(1, gl.BlurCbV!);
            ctx.Draw(3, 0);
        }
        ctx.PSSetShaderResource(0, (ID3D11ShaderResourceView?)null);
        source = gl.BlurBSrv;
    }
    using var back = gl.Swap.GetBuffer<ID3D11Texture2D>(0);
    using var rtv = device.CreateRenderTargetView(back);
    ctx.OMSetRenderTargets(rtv);
    ctx.RSSetViewport(new Viewport(g.W, g.H));
    ctx.PSSetShader(psUw);
    ctx.PSSetShaderResource(0, source); ctx.PSSetConstantBuffer(0, gl.Cb);
    ctx.Draw(3, 0);
    ctx.PSSetShaderResource(0, (ID3D11ShaderResourceView?)null);
}

void Render(Glass gl)
{
    var g = gl.Cur;
    if (g is null) return;
    var mon = mons[g.Mon];
    bool uw = gl.Style == GlassStyle.UWidgets;
    float blurPx = uw ? uwBlur / 100f * 36f : 0f; // same radius scale as uWidgets
    lock (ctx!)
    {
        if (gl.Dead) return;
        if (gl.Applied is null || !gl.Applied.SameShape(g))
        {
            ctx.ClearState();
            if (gl.Applied is null || gl.Applied.W != g.W || gl.Applied.H != g.H || gl.Applied.M != g.M || gl.Strip is null)
            {
                gl.Srv?.Dispose(); gl.Strip?.Dispose();
                gl.Swap.ResizeBuffers(0, (uint)g.W, (uint)g.H, Format.Unknown, SwapChainFlags.None);
                gl.Strip = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)g.W, (uint)(g.H + g.M), 1, 1, BindFlags.ShaderResource));
                gl.Srv = device.CreateShaderResourceView(gl.Strip);
                if (uw && blurPx >= 0.05f)
                {
                    gl.BlurASrv?.Dispose(); gl.BlurBSrv?.Dispose(); gl.BlurA?.Dispose(); gl.BlurB?.Dispose(); gl.BlurCbH?.Dispose(); gl.BlurCbV?.Dispose();
                    var bd = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)g.W, (uint)(g.H + g.M), 1, 1, BindFlags.ShaderResource | BindFlags.RenderTarget);
                    gl.BlurA = device.CreateTexture2D(bd); gl.BlurB = device.CreateTexture2D(bd);
                    gl.BlurASrv = device.CreateShaderResourceView(gl.BlurA); gl.BlurBSrv = device.CreateShaderResourceView(gl.BlurB);
                    gl.BlurCbH = device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<CbBlur>(), BindFlags.ConstantBuffer));
                    gl.BlurCbV = device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<CbBlur>(), BindFlags.ConstantBuffer));
                }
            }
            if (uw)
            {
                var cbU = MakeCbUw(g);
                ctx.UpdateSubresource(in cbU, gl.Cb);
                if (blurPx >= 0.05f && gl.BlurCbH is not null && gl.BlurCbV is not null)
                {
                    var size = new Vector4(g.W, g.H + g.M, 0, 0);
                    var bh = new CbBlur { Params = new Vector4(blurPx, 1, 0, 0), Size = size };
                    var bv = new CbBlur { Params = new Vector4(blurPx, 0, 1, 0), Size = size };
                    ctx.UpdateSubresource(in bh, gl.BlurCbH);
                    ctx.UpdateSubresource(in bv, gl.BlurCbV);
                }
            }
            else
            {
                var cbData = MakeCb(g);
                ctx.UpdateSubresource(in cbData, gl.Cb);
            }
        }
        gl.Applied = g;
        // slice of the monitor frame under the window (clamped to that monitor; the shader samples it with window-local coords)
        int sx = g.X - mon.X, sy = g.Y - mon.Y;
        int l = Math.Max(0, sx), t = Math.Max(0, sy), r = Math.Min(mon.W, sx + g.W), b = Math.Min(mon.H, sy + g.H + g.M);
        if (r <= l || b <= t || gl.Strip is null || gl.Srv is null || mon.Tex is null) return;
        ctx.CopySubresourceRegion(gl.Strip, 0, (uint)(l - sx), (uint)(t - sy), 0, mon.Tex, 0, new Box(l, t, 0, r, b, 1));
        if (uw)
        {
            DrawUw(gl, g, blurPx);
        }
        else
        {
            using var back = gl.Swap.GetBuffer<ID3D11Texture2D>(0);
            using var rtv = device.CreateRenderTargetView(back);
            ctx.OMSetRenderTargets(rtv);
            ctx.RSSetViewport(new Viewport(g.W, g.H));
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.VSSetShader(vs); ctx.PSSetShader(ps);
            ctx.PSSetShaderResource(0, gl.Srv); ctx.PSSetSampler(0, sampler); ctx.PSSetConstantBuffer(0, gl.Cb);
            ctx.Draw(3, 0);
        }
        gl.Swap.Present(0, PresentFlags.None);
    }
}

// --- capture: one session per monitor ---
Native.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr insp).CheckError();
var winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(insp);
var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
var itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
var texIid = typeof(ID3D11Texture2D).GUID;
try { await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless); } catch (Exception e) { Console.WriteLine($"borderless access: {e.Message}"); }

long lastErrorRestart = -100000;
void HandleError(Exception ex)
{
    if (st.Resetting) return;
    int hr = ex.HResult;
    if (hr == unchecked((int)0x887A0005) || hr == unchecked((int)0x887A0006) || hr == unchecked((int)0x887A0007)) Relaunch("GPU device lost");
    if (Environment.TickCount64 - lastErrorRestart < 10000) return;
    lastErrorRestart = Environment.TickCount64;
    Log($"error: {ex.GetType().Name}: {ex.Message}");
    st.Request("error");
}

void StartMon(Mon m)
{
    var item = GraphicsCaptureItem.FromAbi(interop.CreateForMonitor(m.Handle, in itemIid));
    item.Closed += (_, _) => { if (!st.Resetting) st.Request("capture item closed"); };
    var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
    var session = pool.CreateCaptureSession(item);
    session.IsCursorCaptureEnabled = false;
    try { session.IsBorderRequired = false; } catch (Exception e) { Console.WriteLine($"IsBorderRequired: {e.Message}"); }
    // half of the frame period, so timer jitter never makes a frame miss its vsync slot
    session.MinUpdateInterval = TimeSpan.FromSeconds(0.5 / fpsCap);
    session.DirtyRegionMode = GraphicsCaptureDirtyRegionMode.ReportOnly;
    pool.FrameArrived += (sender, _) =>
    {
        try
        {
        if (st.Resetting) return;
        using var frame = sender.TryGetNextFrame();
        if (frame is null || st.Paused || st.Frozen) return; // paused (fullscreen app) or screenshot: no copies, no rendering
        using var tex = new ID3D11Texture2D(frame.Surface.As<IDirect3DDxgiInterfaceAccess>().GetInterface(in texIid));
        lock (ctx!) ctx.CopyResource(m.Tex!, tex); // newest full frame, so a moved/resized window can re-render without a new capture
        foreach (var gl in st.Glasses)
        {
            var g = gl.Cur;
            if (g is null || !ReferenceEquals(mons[g.Mon], m)) continue; // that glass is on another monitor
            bool hit = gl.NeedRender;
            if (!hit)
            {
                int gx = g.X - m.X, gy = g.Y - m.Y;
                foreach (var r in frame.DirtyRegions)
                    if (r.X < gx + g.W && r.X + r.Width > gx && r.Y < gy + g.H + g.M && r.Y + r.Height > gy) { hit = true; break; }
            }
            if (!hit) { Interlocked.Increment(ref skipped); continue; }
            gl.NeedRender = false;
            Render(gl);
            Interlocked.Increment(ref processed);
        }
        }
        catch (Exception ex) { HandleError(ex); }
    };
    session.StartCapture();
    m.Pool = pool; m.Session = session;
}
foreach (var m in mons) StartMon(m);

// Rebuild the capture sessions and the monitor list (after sleep, lock screen / screensaver, display changes).
// The glass windows and the D3D device stay as they are.
void RestartCapture(string reason)
{
    Log($"restarting capture: {reason}");
    st.Resetting = true;
    try
    {
        foreach (var gl in st.Glasses) gl.Cur = null;
        foreach (var m in mons)
        {
            try { m.Session?.Dispose(); } catch { }
            try { m.Pool?.Dispose(); } catch { }
        }
        Thread.Sleep(100); // let running callbacks drain
        lock (ctx!)
        {
            foreach (var m in mons) { try { m.Tex?.Dispose(); } catch { } }
            mons.Clear();
            mons.AddRange(EnumMonitors());
            foreach (var m in mons)
                m.Tex = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)m.W, (uint)m.H, 1, 1, BindFlags.None));
        }
        foreach (var m in mons) StartMon(m);
        foreach (var gl in st.Glasses) gl.NeedRender = true;
        Log("capture restarted: " + string.Join(", ", mons.Select(x => $"{x.W}x{x.H}@{x.X},{x.Y}")));
    }
    catch (Exception ex)
    {
        Log($"capture restart failed: {ex.Message}");
        st.Request("retry", 3000);
    }
    finally { st.Resetting = false; }
}

// false while the secure desktop (lock screen, screensaver with sign-in) has the input
bool InputDesktopAccessible()
{
    var d = Native.OpenInputDesktop(0, false, 0x0001); // DESKTOP_READOBJECTS
    if (d == 0) return false;
    Native.CloseDesktop(d);
    return true;
}
bool lastInputOk = true;

// --- find + follow the target windows ---
var sbf = new StringBuilder(256);
List<(nint Hwnd, GlassStyle Style)> FindTargets()
{
    var res = new List<(nint Hwnd, GlassStyle Style)>();
    Native.EnumWindows((h, _) =>
    {
        if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true; // skip hidden, minimized
        bool owned = Native.GetWindow(h, 4) != 0; // dialogs are owned windows
        bool tool = (Native.GetWindowLongW(h, -20) & 0x80) != 0;
        if (Native.DwmGetWindowAttributeInt(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;
        Native.GetWindowThreadProcessId(h, out uint pid);
        string n = ProcName(pid);
        foreach (var sp in specs)
        {
            if (sp.Proc != "*" && !n.Equals(sp.Proc, StringComparison.OrdinalIgnoreCase)) continue;
            bool strict = sp.Cls is null && sp.Title is null; // a bare process name: skip tool windows and small helper windows
            if (strict && (tool || owned)) continue; // owned dialogs / tool windows only when a class or title is given
            if (sp.Cls is not null)
            {
                sbf.Clear(); Native.GetClassNameW(h, sbf, 256);
                string c = sbf.ToString();
                bool ok = sp.Cls.EndsWith('*') ? c.StartsWith(sp.Cls[..^1], StringComparison.OrdinalIgnoreCase) : c.Equals(sp.Cls, StringComparison.OrdinalIgnoreCase);
                if (!ok) continue;
            }
            if (sp.Title is not null)
            {
                sbf.Clear(); Native.GetWindowTextW(h, sbf, 256);
                if (!sbf.ToString().Contains(sp.Title, StringComparison.OrdinalIgnoreCase)) continue;
            }
            if (Native.DwmGetWindowAttribute(h, 9, out Native.RECT r, Marshal.SizeOf<Native.RECT>()) != 0) continue;
            if (r.right - r.left < (strict ? 200 : 100) || r.bottom - r.top < (strict ? 150 : 30)) continue;
            res.Add((h, Resolve(sp.Style)));
            break;
        }
        return true;
    }, 0);
    return res;
}

Geo? Compute(nint t)
{
    if (t == 0 || !Native.IsWindow(t) || !Native.IsWindowVisible(t) || Native.IsIconic(t)) return null;
    if (Native.DwmGetWindowAttribute(t, 9, out Native.RECT r, Marshal.SizeOf<Native.RECT>()) != 0) return null; // 9 = visible frame (no invisible resize borders)
    int tw = r.right - r.left, th = r.bottom - r.top;
    if (tw < 50 || th < 30) return null;
    // the monitor that holds the window's centre is the one we refract from
    int cx = (r.left + r.right) / 2, cy = (r.top + r.bottom) / 2;
    int mIdx = mons.FindIndex(x => cx >= x.X && cx < x.X + x.W && cy >= x.Y && cy < x.Y + x.H);
    if (mIdx < 0) return null;
    return new Geo(r.left - pad, r.top - pad, tw, th, pad, M, Native.IsZoomed(t) ? 0 : corner, thick, mIdx);
}

void UpdateGlass(Glass gl)
{
    var g = Compute(gl.Target);
    if (g is null)
    {
        if (gl.Shown) { Native.ShowWindow(gl.Hwnd, 0); gl.Shown = false; }
        gl.Cur = null;
        return;
    }
    var old = gl.Cur;
    if (old is null || !gl.Shown || g != old)
    {
        if (old is null || !old.SameShape(g)) ApplyRegion(gl.Hwnd, g);
        Native.SetWindowPos(gl.Hwnd, gl.Target, g.X, g.Y, g.W, g.H, 0x10); // right below the target, no activation
        gl.Cur = g; gl.NeedRender = true;
        if (!gl.Shown) { Native.ShowWindow(gl.Hwnd, 4); gl.Shown = true; }
        Render(gl);
    }
    else if (Native.GetWindow(gl.Target, 2) != gl.Hwnd) // z-order drifted (target was re-activated): put the glass back right under it
        Native.SetWindowPos(gl.Hwnd, gl.Target, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
}

bool IsFullscreenForeground()
{
    var fg = Native.GetForegroundWindow();
    if (fg == 0 || !Native.IsWindowVisible(fg) || Native.IsIconic(fg)) return false;
    sbf.Clear(); Native.GetClassNameW(fg, sbf, 256);
    string c = sbf.ToString();
    if (c is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false; // desktop and taskbar
    Native.RECT r;
    if (Native.DwmGetWindowAttribute(fg, 9, out r, Marshal.SizeOf<Native.RECT>()) != 0 && !Native.GetWindowRect(fg, out r)) return false;
    var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
    if (!Native.GetMonitorInfoW(Native.MonitorFromWindow(fg, 2), ref mi)) return false;
    bool covers = r.left <= mi.rcMonitor.left && r.top <= mi.rcMonitor.top && r.right >= mi.rcMonitor.right && r.bottom >= mi.rcMonitor.bottom;
    if (!covers) return false;
    const int WS_CAPTION = 0x00C00000;
    if (Native.IsZoomed(fg) && (Native.GetWindowLongW(fg, -16) & WS_CAPTION) == WS_CAPTION) return false; // normal maximized window (e.g. auto-hide taskbar)
    return true; // exclusive fullscreen or borderless fullscreen
}

void SetPaused(bool paused)
{
    st.Paused = paused;
    foreach (var m in mons) // while paused the capture only ticks once a second, so it barely costs the game anything
        if (m.Session is not null) { try { m.Session.MinUpdateInterval = paused ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(0.5 / fpsCap); } catch { st.Request("capture session unusable"); } }
    if (!paused) foreach (var gl in st.Glasses) gl.NeedRender = true;
    Log(paused ? "fullscreen app in focus: glass paused (static)" : "glass resumed");
}

Console.WriteLine($"cap {fpsCap} fps, corner {cornerArg}, thick {thickArg}, pad {padArg}, style {style}{(uwOk ? " (uWidgets style available)" : "")}; monitors: " + string.Join(", ", mons.Select(x => $"{x.W}x{x.H}@{x.X},{x.Y}")));
Console.WriteLine("targets: " + string.Join(", ", specs.Select(x => x.Proc + (x.Cls is null ? "" : ":" + x.Cls) + (x.Title is null ? "" : ":" + x.Title) + (x.Style is null ? "" : "=" + x.Style))) + ". Ctrl+C to stop.");
Console.WriteLine("sec | rendered | skipped | glass windows");

var glasses = new Dictionary<nint, Glass>(); // by target hwnd
var clock = Stopwatch.StartNew();
long lastFind = -1000, lastFs = -1000;

// --- screenshots: while Win+Shift+S / PrtScn is active the glass and the app windows become visible to capture ---
// The glass is frozen first (it keeps its last render), then glass windows drop WDA_EXCLUDEFROMCAPTURE and the
// Windhawk mod is told (named event) to do the same for the apps.
var shotEvent = Native.CreateEventW(0, true, false, "Local\\GlassFollow.Screenshot");
Native.ResetEvent(shotEvent); // a stale signalled event from a previous run must not leak in
long shotStart = 0, shotPoll = 0, shotGone = -1;
bool shotSeen = false;

bool OverlayRunning()
{
    var ps = Process.GetProcessesByName("ScreenClippingHost"); // the Win+Shift+S selection overlay
    bool running = ps.Length > 0;
    foreach (var p in ps) p.Dispose();
    return running;
}

void BeginShot()
{
    shotStart = clock.ElapsedMilliseconds;
    if (st.Frozen) return; // pressed again: just extend
    st.Frozen = true; shotSeen = false; shotGone = -1; shotPoll = 0;
    Native.SetEvent(shotEvent);
    foreach (var gl in st.Glasses) Native.SetWindowDisplayAffinity(gl.Hwnd, 0);
    Console.WriteLine("screenshot: glass and apps are visible to capture");
}

void EndShot()
{
    foreach (var gl in st.Glasses) Native.SetWindowDisplayAffinity(gl.Hwnd, 0x11);
    Native.ResetEvent(shotEvent);
    st.Frozen = false;
    foreach (var gl in st.Glasses) gl.NeedRender = true;
    Console.WriteLine("screenshot done: glass resumed");
}

void UpdateShot()
{
    long now = clock.ElapsedMilliseconds;
    if (now - shotPoll < 200) return;
    shotPoll = now;
    if (OverlayRunning()) { shotSeen = true; shotGone = -1; }
    else if (shotSeen && shotGone < 0) shotGone = now;
    bool end = now - shotStart > 30000                       // safety limit
        || (!shotSeen && now - shotStart > 3000)             // PrtScn without the overlay
        || (shotSeen && shotGone >= 0 && now - shotGone > 500); // overlay closed
    if (end) EndShot();
}

nint kbHook = 0;
Native.HookProc kbProc = (code, wParam, lParam) =>
{
    if (code >= 0 && (wParam == 0x100 || wParam == 0x104)) // WM_KEYDOWN / WM_SYSKEYDOWN
    {
        int vk = Marshal.ReadInt32(lParam);
        bool win = (Native.GetAsyncKeyState(0x5B) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x5C) & 0x8000) != 0;
        bool shift = (Native.GetAsyncKeyState(0x10) & 0x8000) != 0;
        if ((vk == 0x53 && win && shift) || vk == 0x2C) st.ShotRequested = true; // never swallow the key
    }
    return Native.CallNextHookEx(0, code, wParam, lParam);
};
if (!noShot) kbHook = Native.SetWindowsHookExW(13, kbProc, Native.GetModuleHandleW(null), 0); // WH_KEYBOARD_LL
try { Console.CancelKeyPress += (_, e) => { e.Cancel = true; st.Run = false; }; } catch { } // no console when --hidden
Native.timeBeginPeriod(1);
var timer = new Timer(_ =>
{
    long p = Interlocked.Exchange(ref processed, 0), k = Interlocked.Exchange(ref skipped, 0);
    var arr = st.Glasses;
    Console.WriteLine($"{DateTime.Now:HH:mm:ss} | {p,8} | {k,7} | {arr.Count(x => x.Cur is not null)} visible / {arr.Length}{(st.Paused ? " (paused)" : "")}");
}, null, 1000, 1000);

while (st.Run)
{
    while (Native.PeekMessageW(out var msg, 0, 0, 0, 1)) { Native.TranslateMessage(ref msg); Native.DispatchMessageW(ref msg); }
    try
    {
    if (st.ShotRequested) { st.ShotRequested = false; BeginShot(); }
    if (st.Frozen) { UpdateShot(); Thread.Sleep(5); continue; } // glass stays exactly as it was
    if (clock.ElapsedMilliseconds - lastFs > 200)
    {
        lastFs = clock.ElapsedMilliseconds;
        bool inputOk = InputDesktopAccessible();
        if (inputOk && !lastInputOk) st.Request("desktop returned (lock screen / screensaver)");
        lastInputOk = inputOk;
        bool fs = IsFullscreenForeground();
        if (fs != st.Paused) SetPaused(fs);
    }
    if (st.RestartRequested && !st.Paused && Environment.TickCount64 >= st.RestartAt) { st.RestartRequested = false; RestartCapture(st.Reason); }
    if (st.Paused) { Thread.Sleep(50); continue; } // glass windows stay exactly as they were
    if (clock.ElapsedMilliseconds - lastFind > 100)
    {
        lastFind = clock.ElapsedMilliseconds;
        var found = FindTargets();
        bool changed = false;
        foreach (var (t, tStyle) in found)
            if (!glasses.ContainsKey(t)) { glasses[t] = CreateGlass(t, tStyle); changed = true; }
        var gone = glasses.Where(kv => !Native.IsWindow(kv.Key)).ToList();
        foreach (var kv in gone) { glasses.Remove(kv.Key); changed = true; }
        if (changed) st.Glasses = glasses.Values.ToArray(); // publish first, then dispose the removed ones
        foreach (var kv in gone) DestroyGlass(kv.Value);
    }
    foreach (var gl in glasses.Values) UpdateGlass(gl);
    Thread.Sleep(1);
    }
    catch (Exception ex) { HandleError(ex); Thread.Sleep(50); }
}
timer.Dispose();
if (kbHook != 0) Native.UnhookWindowsHookEx(kbHook);
GC.KeepAlive(kbProc);
foreach (var gl in glasses.Values) Native.ShowWindow(gl.Hwnd, 0);
foreach (var m in mons) { m.Session?.Dispose(); m.Pool?.Dispose(); }

enum GlassStyle { Correct, UWidgets }

static class Util
{
    // "correct" (default style) / "uw" or "uwidgets" (second style); null = not a style name
    public static GlassStyle? ParseStyle(string v) => v.ToLowerInvariant() switch
    {
        "correct" or "classic" or "default" => GlassStyle.Correct,
        "uw" or "uwidgets" or "uwidget" => GlassStyle.UWidgets,
        _ => null,
    };
}

sealed record Spec(string Proc, string? Cls, string? Title, GlassStyle? Style = null);

sealed class Mon
{
    public nint Handle;
    public int X, Y, W, H;
    public ID3D11Texture2D? Tex;
    public Direct3D11CaptureFramePool? Pool;
    public GraphicsCaptureSession? Session;
}

sealed class Glass
{
    public nint Hwnd, Target;
    public IDXGISwapChain1 Swap = null!;
    public ID3D11Buffer Cb = null!;
    public ID3D11Texture2D? Strip; // window-sized slice of a monitor frame (+M rows below), sampled by the shader
    public ID3D11ShaderResourceView? Srv;
    public GlassStyle Style;
    public ID3D11Texture2D? BlurA, BlurB; // uWidgets style, only with --uw-blur
    public ID3D11ShaderResourceView? BlurASrv, BlurBSrv;
    public ID3D11Buffer? BlurCbH, BlurCbV;
    public Geo? Applied;
    public volatile Geo? Cur;
    public volatile bool NeedRender = true;
    public volatile bool Dead;
    public bool Shown;
}

sealed record Geo(int X, int Y, int TW, int TH, int Pad, int M, float Radius, float Thick, int Mon)
{
    public int W => TW + 2 * Pad;
    public int H => TH + 2 * Pad;
    public bool SameShape(Geo o) => TW == o.TW && TH == o.TH && Pad == o.Pad && M == o.M && Radius == o.Radius && Thick == o.Thick;
}

sealed class State
{
    public volatile Glass[] Glasses = [];
    public volatile bool Run = true;
    public volatile bool Paused; // a fullscreen app has focus: glass stays static
    public volatile bool Frozen; // screenshot in progress: glass stays static and visible to capture
    public volatile bool ShotRequested;
    public volatile bool Resetting;        // capture is being rebuilt: callbacks do nothing
    public volatile bool RestartRequested; // rebuild the capture once things have settled
    public string Reason = "";
    public long RestartAt;
    public void Request(string reason, int delayMs = 1500) { Reason = reason; RestartAt = Environment.TickCount64 + delayMs; RestartRequested = true; }
}

[StructLayout(LayoutKind.Sequential)]
struct CbUw { public Vector4 Widget, Effect, Tint, Bounds, Bounds2, Rim, Light, Angle; }

[StructLayout(LayoutKind.Sequential)]
struct CbBlur { public Vector4 Params, Size; }

[StructLayout(LayoutKind.Sequential)]
struct Cb { public Vector2 WinSize, TexSize; public Vector4 S0, S1, S2, S3, S4, S5, R0, R1, R2, R3, R4, R5, Flags; }

[ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IGraphicsCaptureItemInterop { IntPtr CreateForWindow(IntPtr window, in Guid iid); IntPtr CreateForMonitor(IntPtr monitor, in Guid iid); }

[ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDirect3DDxgiInterfaceAccess { IntPtr GetInterface(in Guid iid); }

static class Native
{
    public delegate nint WndProc(nint h, uint m, nint w, nint l);
    public delegate bool EnumProc(nint h, nint l);
    public delegate bool MonitorEnumProc(nint hMonitor, nint hdc, nint rect, nint data);
    public delegate nint HookProc(int code, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW { public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public nint hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public nint hIconSm; }
    public struct RECT { public int left, top, right, bottom; }
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    public struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public int x, y; }
    [DllImport("kernel32")] public static extern bool FreeConsole();
    [DllImport("user32")] public static extern bool SetProcessDpiAwarenessContext(nint v);
    [DllImport("user32")] public static extern uint GetDpiForSystem();
    [DllImport("user32")] public static extern nint MonitorFromPoint(System.Drawing.Point p, uint flags);
    [DllImport("user32")] public static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc proc, nint data);
    [DllImport("user32")] public static extern bool GetMonitorInfoW(nint m, ref MONITORINFO mi);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern nint CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [DllImport("user32")] public static extern bool DestroyWindow(nint h);
    [DllImport("user32")] public static extern nint DefWindowProcW(nint h, uint m, nint w, nint l);
    [DllImport("user32")] public static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32")] public static extern bool SetWindowDisplayAffinity(nint h, uint a);
    [DllImport("user32")] public static extern bool PeekMessageW(out MSG m, nint h, uint a, uint b, uint remove);
    [DllImport("user32")] public static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32")] public static extern nint DispatchMessageW(ref MSG m);
    [DllImport("user32")] public static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32")] public static extern int SetWindowRgn(nint h, nint rgn, bool redraw);
    [DllImport("user32")] public static extern bool EnumWindows(EnumProc p, nint l);
    [DllImport("user32")] public static extern bool IsWindow(nint h);
    [DllImport("user32")] public static extern bool IsWindowVisible(nint h);
    [DllImport("user32")] public static extern bool IsIconic(nint h);
    [DllImport("user32")] public static extern bool IsZoomed(nint h);
    [DllImport("user32")] public static extern nint GetForegroundWindow();
    [DllImport("user32")] public static extern nint MonitorFromWindow(nint h, uint flags);
    [DllImport("user32")] public static extern bool GetWindowRect(nint h, out RECT r);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern nint SetWindowsHookExW(int id, HookProc proc, nint mod, uint thread);
    [DllImport("user32")] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32")] public static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32")] public static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32")] public static extern bool CloseDesktop(nint desktop);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandleW(string? name);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] public static extern nint CreateEventW(nint attrs, bool manualReset, bool initial, string name);
    [DllImport("kernel32")] public static extern bool SetEvent(nint h);
    [DllImport("kernel32")] public static extern bool ResetEvent(nint h);
    [DllImport("user32")] public static extern nint GetWindow(nint h, uint cmd);
    [DllImport("user32")] public static extern int GetWindowLongW(nint h, int index);
    [DllImport("user32")] public static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(nint h, StringBuilder sb, int max);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(nint h, StringBuilder sb, int max);
    [DllImport("dwmapi", EntryPoint = "DwmGetWindowAttribute")] public static extern int DwmGetWindowAttribute(nint h, int attr, out RECT value, int size);
    [DllImport("dwmapi", EntryPoint = "DwmGetWindowAttribute")] public static extern int DwmGetWindowAttributeInt(nint h, int attr, out int value, int size);
    [DllImport("winmm")] public static extern uint timeBeginPeriod(uint ms);
    [DllImport("gdi32")] public static extern nint CreateRoundRectRgn(int a, int b, int c, int d, int w, int h);
    [DllImport("d3d11")] public static extern SharpGen.Runtime.Result CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}
