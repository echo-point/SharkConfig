using System.Runtime.InteropServices;
using SharkConfig.Protocol;
using SharkConfig.Transport;

namespace SharkConfig;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--selftest")) return SelfTest.Run();
        if (args.Contains("--list"))
        {
            AttachConsole(-1);
            foreach (var d in WinHid.Enumerate()) Console.WriteLine($"{d}\n    {d.Path}");
            return 0;
        }
        ApplicationConfiguration.Initialize();
        // --sim: conecta no simulador; --page overview|performance|dpi|buttons|lighting|advanced
        var page = args.SkipWhile(a => a != "--page").Skip(1).FirstOrDefault();
        Application.Run(new UI.HostForm(args.Contains("--sim"), page));
        return 0;
    }

    [DllImport("kernel32.dll")] internal static extern bool AttachConsole(int pid);
}

/// <summary>Testes do protocolo contra o simulador: `SharkConfig.exe --selftest`.</summary>
static class SelfTest
{
    static int _fail;

    static bool Throws(Action a) { try { a(); return false; } catch (Exception) { return true; } }

    static void Check(bool ok, string name)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
        if (!ok) _fail++;
    }

    public static int Run()
    {
        Program.AttachConsole(-1);
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine();

        // 1. checksum/pacote — ReadVersion: 08 12 00.. → 0x55-0x1A = 0x3B
        var p = Packet.Build(Cmd.Version);
        Check(p.Length == 17 && p[0] == 0x08 && p[1] == 0x12 && p[16] == 0x3B, "pacote ReadVersion + checksum 0x3B");
        var w = Packet.Build(Cmd.WriteFlash, 0x00, [0x01, 0x54]);
        Check(Packet.Hex(w) == "08 07 00 00 00 02 01 54 00 00 00 00 00 00 00 00 EF", "pacote escrita polling 1000Hz = " + Packet.Hex(w));

        // 2. política de segurança
        bool blocked;
        try { Packet.Build((Cmd)0x0D); blocked = false; } catch (InvalidOperationException) { blocked = true; }
        Check(blocked, "opcode 0x0D (DFU) bloqueado");
        Check(!Flash.IsWritable(0x0300, 2) && !Flash.IsWritable(0x0100, 2) && Flash.IsWritable(0x1B00, 6) && !Flash.IsWritable(0xB3, 2),
            "faixas de escrita (atalhos, macros e 0xB3 bloqueados)");

        // 3. DPI encode/decode (round-trip em todo o range com os passos da UI)
        bool rt = true; int bad = 0;
        for (int dpi = 50; dpi <= 42000; dpi += dpi < 30000 ? 50 : 100)
        {
            var (raw, flag) = MouseConfig.EncodeDpi(dpi);
            if (MouseConfig.DecodeDpi(raw, flag) != dpi) { rt = false; bad = dpi; break; }
        }
        Check(rt, "DPI round-trip 50..42000" + (rt ? "" : $" (falhou em {bad})"));
        var e = MouseConfig.EncodeDpi(42000);
        Check(e.Raw == 20999 && (e.Flag & 0x11) == 0x11, $"DPI 42000 → raw {e.Raw} flag 0x{e.Flag:X2}");

        // 4. leitura completa no simulador
        var sim = new SimulatedMouse();
        using var dev = new MouseDevice(sim);
        Check(dev.ReadVersion() == 0x0102, "versão via simulador");
        Check(dev.ReadBattery() is { Percent: 87, Millivolts: 3950 }, "bateria via simulador");
        var img = dev.ReadFlash();
        var cfg = MouseConfig.FromImage(img);
        Check(cfg.Warnings.Count == 0, "flash padrão sem avisos (" + string.Join("; ", cfg.Warnings) + ")");
        Check(cfg.PollingHz == 1000 && cfg.StageCount == 6 && cfg.CurrentStage == 2 && cfg.DebounceMs == 8 && cfg.MotionSync,
            "valores padrão (1000Hz, 6 estágios, atual 3, 8ms, MotionSync)");
        Check(cfg.Dpi[0] == 1200 && cfg.Dpi[5] == 42000, $"DPIs padrão {string.Join(",", cfg.Dpi)}");

        // 5. escrita diferencial
        cfg.PollingRateCode = 0x40; // 8000 Hz
        cfg.Dpi[1] = 1600;
        cfg.AngleSnap = true;
        cfg.Buttons[4] = new ButtonAction(ButtonType.Dpi, 0x02, 0);
        var ops = MouseConfig.Diff(img, cfg.ApplyTo(img));
        Check(ops.Count == 4, $"diff gera 4 escritas (gerou {ops.Count}: {string.Join(" | ", ops.Select(o => $"{o.Address:X4}"))})");
        int n = dev.Apply(cfg, Path.Combine(Path.GetTempPath(), "sharkconfig-selftest"));
        var again = MouseConfig.FromImage(new MouseDevice(sim).ReadFlash());
        Check(n == 4 && again.PollingHz == 8000 && again.Dpi[1] == 1600 && again.AngleSnap && again.Buttons[4].Type == ButtonType.Dpi,
            "aplicar + reler no simulador");
        Check(MouseConfig.Diff(sim.Flash, again.ApplyTo(sim.Flash)).Count == 0, "reaplicar sem mudanças = 0 escritas");

        // 6. função de botão que a UI não oferece (ex.: mídia) é preservada sem escrita
        sim.Flash.SetRecord(Flash.Button(3), [(byte)ButtonType.Combo, 0, 0]);
        var keep = MouseConfig.FromImage(new MouseDevice(sim).ReadFlash());
        var keepDto = UI.ConfigDto.From(keep);
        var keepBack = MouseConfig.FromImage(sim.Flash); keepDto.ApplyTo(keepBack);
        Check(keepDto.Buttons[3].StartsWith("custom:") && MouseConfig.Diff(sim.Flash, keepBack.ApplyTo(sim.Flash)).Count == 0,
            "função não suportada é mantida intacta");

        // 7. ponte da UI web: config → JSON → config não pode gerar escritas
        var baseImg = sim.Flash.Clone();
        var json = System.Text.Json.JsonSerializer.Serialize(UI.ConfigDto.From(MouseConfig.FromImage(baseImg)), UI.Bridge.Json);
        var dto = System.Text.Json.JsonSerializer.Deserialize<UI.ConfigDto>(json, UI.Bridge.Json)!;
        var round = MouseConfig.FromImage(baseImg); dto.ApplyTo(round);
        Check(MouseConfig.Diff(baseImg, round.ApplyTo(baseImg)).Count == 0, "JSON da UI ida e volta = 0 escritas");
        dto.PollingCode = 0x08; dto.Buttons[2] = "dpi-up"; dto.Colors[0] = "#123456";
        var edited = MouseConfig.FromImage(baseImg); dto.ApplyTo(edited);
        Check(MouseConfig.Diff(baseImg, edited.ApplyTo(baseImg)).Count == 3, "JSON da UI com 3 mudanças = 3 escritas");

        // 8. conformidade com o app oficial e proteções contra gravação errada
        string tmp = Path.Combine(Path.GetTempPath(), "sharkconfig-selftest");
        (SimulatedMouse s, MouseDevice d, MouseConfig c, UI.ConfigDto dto) Fresh()
        {
            var s = new SimulatedMouse(); var d = new MouseDevice(s); var c = MouseConfig.FromImage(d.ReadFlash());
            return (s, d, c, UI.ConfigDto.From(c));
        }

        var f = Fresh(); f.dto.LedEffect = 0; f.dto.ApplyTo(f.c);
        var ledOps = MouseConfig.Diff(f.d.Image!, f.c.ApplyTo(f.d.Image!)).Select(o => o.Address).ToList();
        Check(ledOps.Contains(Flash.DpiLedEffect) && ledOps.Contains(Flash.DpiLedOn), "LED desligado também grava 'LED ligado' = 0 (0x52)");

        f = Fresh(); f.dto.StageCount = 8; f.dto.Debounce = 0; f.dto.LedSpeed = 9; f.dto.LedBrightness = 10; f.dto.ApplyTo(f.c);
        Check(f.c.StageCount == 6 && f.c.DebounceMs == 1 && f.c.DpiLedSpeed == 5 && f.c.DpiLedBrightness == 255,
            $"limites oficiais: estágios≤6, debounce 1–20, velocidade 1–5, brilho em níveis ({f.c.StageCount},{f.c.DebounceMs},{f.c.DpiLedSpeed},{f.c.DpiLedBrightness})");

        f = Fresh(); f.dto.PollingCode = 0x03;
        Check(Throws(() => f.dto.ApplyTo(f.c)), "taxa de atualização fora da lista é recusada");

        f = Fresh(); f.dto.Buttons[0] = "dpi-up";
        Check(Throws(() => f.dto.ApplyTo(f.c)), "não deixa ficar sem clique esquerdo");

        f = Fresh(); f.s.Flash.Data[Flash.SensorMode + 1] ^= 0xFF; var f2 = new MouseDevice(f.s); var c2 = MouseConfig.FromImage(f2.ReadFlash());
        c2.PollingRateCode = 0x40; int before = f.s.Flash.Data[0];
        Check(Throws(() => f2.Apply(c2, tmp)) && f.s.Flash.Data[0] == before, "leitura com campo inválido bloqueia qualquer gravação");

        f = Fresh(); f.s.DropWriteEcho = true; f.c.PollingRateCode = 0x40;
        Check(Throws(() => f.d.Apply(f.c, tmp)), "escrita sem eco do mouse interrompe e avisa");

        f = Fresh(); f.s.StuckAddress = Flash.PollingRate; f.c.PollingRateCode = 0x40;
        Check(Throws(() => f.d.Apply(f.c, tmp)), "conferência por releitura detecta gravação que não pegou");

        Check(UI.Bridge.ModelIndex(10) == 5 && UI.Bridge.ModelIndex(16) == 11 && UI.Bridge.IsPaw3950Model(UI.Bridge.ModelIndex(10))
              && !UI.Bridge.IsPaw3950Model(UI.Bridge.ModelIndex(16)), "MID decimal como no oficial; DPI só para modelos PAW3950");

        Console.WriteLine(_fail == 0 ? "\nTodos os testes passaram." : $"\n{_fail} teste(s) falharam.");
        return _fail == 0 ? 0 : 1;
    }
}
