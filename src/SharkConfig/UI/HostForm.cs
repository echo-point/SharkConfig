using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SharkConfig.UI;

/// <summary>Janela nativa que hospeda a interface web (wwwroot) via WebView2, configurada para gastar pouco.</summary>
public sealed class HostForm : Form
{
    static readonly Color Base = Color.FromArgb(0x1E, 0x20, 0x23);
    readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Base };
    readonly string _query;
    Bridge? _bridge;

    // Motor mínimo: 1 processo de renderização, sem serviços de fundo do Edge (rede, sync, extensões, updates).
    const string BrowserArgs =
        "--renderer-process-limit=1 --disable-background-networking --disable-component-update " +
        "--disable-sync --disable-extensions --disable-default-apps --no-first-run --disable-breakpad " +
        // UI estática e quase sem blur: compor em software no próprio processo elimina o processo de GPU
        "--disable-gpu --in-process-gpu --disable-software-rasterizer " +
        "--disable-features=msSmartScreenProtection,msWebOOUI,msPdfOOUI,SpareRendererForSitePerProcess," +
        "AutofillServerCommunication,OptimizationHints,Translate,MediaRouter,BackForwardCache " +
        "--js-flags=--max-old-space-size=48";

    public HostForm(bool startSimulator, string? startPage)
    {
        Text = "SharkConfig";
        BackColor = Base;
        MinimumSize = new Size(900, 600);   // abaixo disso a UI escalaria demais
        Size = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_web);
        _query = $"?sim={(startSimulator ? 1 : 0)}&page={Uri.EscapeDataString(startPage ?? "overview")}";
        Load += async (_, _) => await InitAsync();
        Resize += async (_, _) => await OnMinimizeChangedAsync();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int on = 1, caption = ToBgr(Base), text = ToBgr(Color.FromArgb(0xF2, 0xF2, 0xF0)), round = 2;
        DwmSetWindowAttribute(Handle, 20, ref on, 4);       // DWMWA_USE_IMMERSIVE_DARK_MODE
        DwmSetWindowAttribute(Handle, 33, ref round, 4);    // DWMWA_WINDOW_CORNER_PREFERENCE = arredondado
        DwmSetWindowAttribute(Handle, 35, ref caption, 4);  // DWMWA_CAPTION_COLOR
        DwmSetWindowAttribute(Handle, 34, ref caption, 4);  // DWMWA_BORDER_COLOR
        DwmSetWindowAttribute(Handle, 36, ref text, 4);     // DWMWA_TEXT_COLOR
    }

    static int ToBgr(Color c) => c.B << 16 | c.G << 8 | c.R;

    async Task InitAsync()
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharkConfig", "WebView2");
        var args = BrowserArgs + " " + Environment.GetEnvironmentVariable("SHARKCONFIG_WV2_ARGS"); // para experimentos
        var options = new CoreWebView2EnvironmentOptions(args.Trim()) { AreBrowserExtensionsEnabled = false };
        var env = await CoreWebView2Environment.CreateAsync(null, dataDir, options);
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;
        var st = core.Settings;
        st.AreDefaultContextMenusEnabled = false;
        st.IsStatusBarEnabled = false;
        st.IsZoomControlEnabled = false;
        st.IsPinchZoomEnabled = false;
        st.IsSwipeNavigationEnabled = false;
        st.IsPasswordAutosaveEnabled = false;
        st.IsGeneralAutofillEnabled = false;
        st.AreBrowserAcceleratorKeysEnabled = false;
        st.IsBuiltInErrorPageEnabled = false;
        core.SetVirtualHostNameToFolderMapping("sharkconfig.local",
            Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Allow);

        _bridge = new Bridge(
            json => BeginInvoke(() => core.PostWebMessageAsJson(json)),
            f => (string?)Invoke(f));
        core.WebMessageReceived += (_, e) => _ = _bridge.HandleAsync(e.WebMessageAsJson);
        core.Navigate("https://sharkconfig.local/index.html" + _query);
    }

    bool _suspended;
    async Task OnMinimizeChangedAsync()
    {
        var core = _web.CoreWebView2;
        if (core is null) return;
        if (WindowState == FormWindowState.Minimized && !_suspended)
        {
            // minimizado: congela scripts/render e devolve memória ao sistema
            _suspended = true;
            core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            _web.Visible = false;
            try { await core.TrySuspendAsync(); } catch { /* ocupado: tenta no próximo minimizar */ }
            EmptyWorkingSet(Environment.ProcessId);
        }
        else if (WindowState != FormWindowState.Minimized && _suspended)
        {
            _suspended = false;
            _web.Visible = true;   // retomar é automático ao ficar visível
            core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
        }
    }

    static void EmptyWorkingSet(int pid)
    {
        using var p = System.Diagnostics.Process.GetProcessById(pid);
        SetProcessWorkingSetSize(p.Handle, -1, -1);
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr h, nint min, nint max);
}
