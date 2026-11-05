using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SharkConfig.UI;

/// <summary>
/// Janela nativa que hospeda a interface web (wwwroot) via WebView2.
/// Fundo acrílico do Windows 11 atrás de uma página transparente (transparência ajustável),
/// tema escuro/claro na barra de título e detecção automática do mouse por WM_DEVICECHANGE.
/// </summary>
public sealed class HostForm : Form
{
    static readonly Color DarkBase = Color.FromArgb(0x1E, 0x20, 0x23), LightBase = Color.FromArgb(0xF5, 0xF5, 0xF7);
    readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };
    readonly System.Windows.Forms.Timer _deviceDebounce = new() { Interval = 500 };
    readonly string _query;
    readonly AppSettings _settings = AppSettings.Load();
    Bridge? _bridge;

    /// <summary>Mensagem que a segunda execução do app manda (broadcast) para esta janela vir para a frente.</summary>
    public static readonly int ShowWindowMessage = RegisterWindowMessage("SharkConfig.MostrarJanela");

    // 1 processo de renderização e sem serviços de fundo do Edge. A GPU fica ligada: é ela que
    // compõe as animações e o vidro a 60 fps sem custar CPU. O serviço de rede roda dentro do processo
    // principal (a página é só arquivo local): −1 processo, ~9 MB. Medido e NÃO adotado, porque engasga:
    // --in-process-gpu (−70 MB, mas ~110 fps com quadros de 25–37 ms), --disable-gpu-rasterization
    // (−15 MB, quadro de 71 ms ao abrir Botões) e memória Low ao perder o foco (−40 MB, 50–75 ms ao voltar).
    const string BrowserArgs =
        "--renderer-process-limit=1 --enable-features=NetworkServiceInProcess2 " +
        "--disable-background-networking --disable-component-update " +
        "--disable-sync --disable-extensions --disable-default-apps --no-first-run --disable-breakpad " +
        "--disable-features=msSmartScreenProtection,msWebOOUI,msPdfOOUI,SpareRendererForSitePerProcess," +
        "AutofillServerCommunication,OptimizationHints,Translate,MediaRouter,BackForwardCache " +
        "--js-flags=--max-old-space-size=48";

    public HostForm(string? startPage)
    {
        Text = "SharkConfig";
        using (var ico = typeof(HostForm).Assembly.GetManifestResourceStream("SharkConfig.app.ico"))
            if (ico is not null) Icon = new Icon(ico);   // multi-tamanho: o Windows escolhe 16/20/24 (título) e 32/40/48 (barra)
        BackColor = Color.Black;            // preto = área por onde o fundo acrílico aparece
        MinimumSize = new Size(900, 600);   // abaixo disso a UI escalaria demais
        Size = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_web);
        _query = $"?page={Uri.EscapeDataString(startPage ?? "overview")}";
        Load += async (_, _) => await InitAsync();
        Resize += async (_, _) => await OnMinimizeChangedAsync();
        _deviceDebounce.Tick += (_, _) => { _deviceDebounce.Stop(); _ = _bridge?.OnDevicesChangedAsync(); };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = 2;
        DwmSetWindowAttribute(Handle, 33, ref round, 4);    // DWMWA_WINDOW_CORNER_PREFERENCE = arredondado
        var m = new MARGINS { L = -1, R = -1, T = -1, B = -1 };
        DwmExtendFrameIntoClientArea(Handle, ref m);        // o fundo do sistema cobre toda a janela
        ApplyAppearance(_settings);
    }

    /// <summary>Tema e transparência na moldura nativa (barra de título, borda e fundo acrílico).</summary>
    public void ApplyAppearance(AppSettings s)
    {
        if (!IsHandleCreated) return;
        bool dark = s.Theme != "light";
        var baseColor = dark ? DarkBase : LightBase;
        int darkMode = dark ? 1 : 0;
        int backdrop = s.Transparency > 0 ? 3 : 1;          // DWMSBT_TRANSIENTWINDOW (acrílico) | DWMSBT_NONE
        // Barra de título sempre sólida, na cor que o corpo tem: o corpo é a cor do tema (alfa 1−t) sobre
        // o acrílico. Com a barra em acrílico puro ela ficava bem mais clara/escura que o corpo (escuro
        // 60%: 74 × 52). O acrílico varia com o que está atrás (medido de fundo preto a branco: escuro
        // 54–146, claro 135–227); usamos o meio da faixa, o que deixa a diferença máxima em ~28 a 60%.
        double t = s.Transparency / 100.0;
        var acrylicMid = dark ? Color.FromArgb(100, 100, 100) : Color.FromArgb(181, 181, 181);
        int caption = ToBgr(Mix(baseColor, acrylicMid, t));
        int text = ToBgr(dark ? Color.FromArgb(0xF2, 0xF2, 0xF0) : Color.FromArgb(0x1D, 0x1D, 0x1F));
        DwmSetWindowAttribute(Handle, 20, ref darkMode, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
        DwmSetWindowAttribute(Handle, 38, ref backdrop, 4); // DWMWA_SYSTEMBACKDROP_TYPE
        DwmSetWindowAttribute(Handle, 35, ref caption, 4);  // DWMWA_CAPTION_COLOR
        DwmSetWindowAttribute(Handle, 34, ref caption, 4);  // DWMWA_BORDER_COLOR
        DwmSetWindowAttribute(Handle, 36, ref text, 4);     // DWMWA_TEXT_COLOR
    }

    static int ToBgr(Color c) => c.B << 16 | c.G << 8 | c.R;
    static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));

    protected override void WndProc(ref Message m)
    {
        // WM_DEVICECHANGE: chega a toda janela de topo quando algo USB é ligado/desligado.
        // Sem consulta periódica: só reagimos ao aviso do Windows (com 500 ms de espera p/ estabilizar).
        if (m.Msg == 0x0219) { _deviceDebounce.Stop(); _deviceDebounce.Start(); }
        if (m.Msg == ShowWindowMessage && ShowWindowMessage != 0)
        {
            ShowWindow(Handle, IsIconic(Handle) ? 9 : 5);   // SW_RESTORE (volta ao tamanho de antes) | SW_SHOW
            SetForegroundWindow(Handle);
            return;
        }
        base.WndProc(ref m);
    }

    async Task InitAsync()
    {
        try { await StartWebViewAsync(); }
        catch (WebView2RuntimeNotFoundException)
        {
            Fail("O SharkConfig precisa do Microsoft Edge WebView2 Runtime, que não foi encontrado neste PC.\n\n" +
                 "Ele já vem no Windows 11. No Windows 10, instale o \"WebView2 Runtime\" pelo site da Microsoft " +
                 "(developer.microsoft.com/microsoft-edge/webview2) e abra o app de novo.");
        }
        catch (Exception ex)
        {
            Fail("Não foi possível iniciar a interface do SharkConfig (WebView2).\n\n" +
                 "Feche o app, espere alguns segundos e abra de novo. Se continuar, reinstale o WebView2 Runtime.\n\n" +
                 "Detalhe técnico: " + ex.Message);
        }
    }

    void Fail(string text)
    {
        MessageBox.Show(this, text, "SharkConfig", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Close();
    }

    async Task StartWebViewAsync()
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
            f => (string?)Invoke(f),
            _settings,
            s => BeginInvoke(() => ApplyAppearance(s)));
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

    [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int L, R, T, B; }
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr h, nint min, nint max);
}
