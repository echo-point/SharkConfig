using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharkConfig.Protocol;
using SharkConfig.Transport;

namespace SharkConfig.UI;

/// <summary>Configuração no formato da interface web (JSON camelCase).</summary>
public sealed class ConfigDto
{
    public int PollingCode { get; set; }
    public int SensorMode { get; set; }
    public int Lod { get; set; }
    public bool MotionSync { get; set; }
    public bool AngleSnap { get; set; }
    public bool Ripple { get; set; }
    public bool Performance { get; set; }
    public int PerformanceTimeout { get; set; }
    public int Debounce { get; set; }
    public int Sleep { get; set; }
    public int StageCount { get; set; }
    public int CurrentStage { get; set; }
    public int[] Dpi { get; set; } = [];
    public string[] Colors { get; set; } = [];
    public int LedEffect { get; set; }
    public int LedBrightness { get; set; }
    public int LedSpeed { get; set; }
    /// <summary>Id de <see cref="ButtonOption"/> ou "custom:tt-pp-pp" se não mapeado.</summary>
    public string[] Buttons { get; set; } = [];

    public static ConfigDto From(MouseConfig c) => new()
    {
        PollingCode = c.PollingRateCode, SensorMode = c.SensorMode, Lod = c.Lod, MotionSync = c.MotionSync,
        AngleSnap = c.AngleSnap, Ripple = c.Ripple, Performance = c.Performance,
        PerformanceTimeout = c.PerformanceTimeoutUnits, Debounce = c.DebounceMs, Sleep = c.SleepUnits,
        StageCount = c.StageCount, CurrentStage = c.CurrentStage, Dpi = (int[])c.Dpi.Clone(),
        Colors = c.DpiColors.Select(x => $"#{x.R:X2}{x.G:X2}{x.B:X2}").ToArray(),
        LedEffect = c.DpiLedEffect, LedBrightness = c.DpiLedBrightness, LedSpeed = c.DpiLedSpeed,
        Buttons = c.Buttons.Select(b => ButtonOption.Find(b)?.Id ?? $"custom:{(byte)b.Type:X2}-{b.P1:X2}-{b.P2:X2}").ToArray(),
    };

    public void ApplyTo(MouseConfig c)
    {
        c.PollingRateCode = (byte)PollingCode; c.SensorMode = Math.Clamp(SensorMode, 0, 2); c.Lod = Math.Clamp(Lod, 0, 2);
        c.MotionSync = MotionSync; c.AngleSnap = AngleSnap; c.Ripple = Ripple; c.Performance = Performance;
        c.PerformanceTimeoutUnits = (byte)PerformanceTimeout; c.DebounceMs = Math.Clamp(Debounce, 0, 30); c.SleepUnits = (byte)Sleep;
        c.StageCount = Math.Clamp(StageCount, 1, Flash.MaxStages);
        c.CurrentStage = Math.Clamp(CurrentStage, 0, c.StageCount - 1);
        for (int i = 0; i < Flash.MaxStages && i < Dpi.Length; i++) c.Dpi[i] = MouseConfig.SnapDpi(Dpi[i]);
        for (int i = 0; i < Flash.MaxStages && i < Colors.Length; i++) c.DpiColors[i] = ColorTranslator.FromHtml(Colors[i]);
        c.DpiLedEffect = Math.Clamp(LedEffect, 0, 2); c.DpiLedBrightness = Math.Clamp(LedBrightness, 0, 255); c.DpiLedSpeed = Math.Clamp(LedSpeed, 0, 10);
        for (int i = 0; i < c.Buttons.Length && i < Buttons.Length; i++)
            if (ButtonOption.All.FirstOrDefault(o => o.Id == Buttons[i]) is { } opt) c.Buttons[i] = opt.ToAction();
    }
}

/// <summary>
/// Ponte JS ↔ C#. Mensagens: { id, cmd, args } → { id, ok, result | error }.
/// Eventos empurrados: { event: "log" | "progress", ... }.
/// </summary>
public sealed class Bridge
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    readonly Action<string> _post;
    readonly Func<Func<string?>, string?> _onUi; // executa diálogos na thread de UI
    readonly string _backupDir = Path.Combine(AppContext.BaseDirectory, "backups");
    readonly SemaphoreSlim _lock = new(1, 1);
    MouseDevice? _dev;
    BatteryInfo? _battery;
    ushort? _version;
    (byte Cid, byte Mid)? _model;

    // MIDs do Config.ini do app oficial na ordem Drive1..18 → foto img/mouse{i}.png (mesma convenção do app oficial)
    static readonly string[] ModelMids = ["01","02","03","04","05","10","11","12","13","14","15","16","17","18","19","20","21","22"];
    static int? ModelIndex(byte mid)
    {
        int i = Array.FindIndex(ModelMids, s => Convert.ToInt32(s, 16) == mid);
        if (i < 0) i = Array.FindIndex(ModelMids, s => int.Parse(s) == mid);
        return i >= 0 ? i : null;
    }

    public Bridge(Action<string> post, Func<Func<string?>, string?> onUi) { _post = post; _onUi = onUi; }

    public async Task HandleAsync(string message)
    {
        var msg = JsonNode.Parse(message)!;
        var id = msg["id"]?.GetValue<int>() ?? 0;
        var cmd = msg["cmd"]?.GetValue<string>() ?? "";
        var args = msg["args"];
        object? result; string? error = null;
        await _lock.WaitAsync();
        try { result = await Task.Run(() => Dispatch(cmd, args)); }
        catch (Exception ex) { result = null; error = ex.Message; Log("Erro: " + ex.Message); }
        finally { _lock.Release(); }
        _post(JsonSerializer.Serialize(new { id, ok = error is null, result, error }, Json));
    }

    object? Dispatch(string cmd, JsonNode? args) => cmd switch
    {
        "init" => Init(),
        "connectSim" => Connect(new MouseDevice(new SimulatedMouse())),
        "connectReal" => ConnectReal(),
        "read" => Read(),
        "defaults" => ConfigDto.From(MouseConfig.FromImage(MouseConfig.DefaultImage())),
        "preview" => Preview(Dto(args)),
        "apply" => Apply(Dto(args)),
        "exportBackup" => _dev?.Image is { } img ? MouseDevice.SaveBackup(img, _backupDir) : throw new InvalidOperationException("Leia o mouse antes de exportar."),
        "importBackup" => ImportBackup(),
        _ => throw new InvalidOperationException("comando desconhecido: " + cmd),
    };

    static ConfigDto Dto(JsonNode? n) => n.Deserialize<ConfigDto>(Json) ?? throw new ArgumentException("config ausente");

    object Init() => new
    {
        pollingRates = MouseConfig.PollingRates.Select(p => new { code = p.Code, hz = p.Hz }),
        sleepOptions = MouseConfig.SleepOptions.Select(s => new { units = s.Units, label = s.Label }),
        lod = MouseConfig.LodLabels,
        sensorModes = new[] { "LP", "HP", "Corded" },
        ledEffects = MouseConfig.DpiLedEffects,
        buttons = ButtonOption.All.Select(b => new { b.Id, b.Group, b.Label }),
        dpi = new { min = MouseConfig.DpiMin, max = MouseConfig.DpiMax, seg1 = MouseConfig.DpiSeg1Max, maxStages = Flash.MaxStages },
        version = typeof(Bridge).Assembly.GetName().Version?.ToString(3),
    };

    object? ConnectReal()
    {
        var dev = MouseDevice.TryOpenReal(out var message);
        Log(message);
        if (dev is null) throw new InvalidOperationException(
            "Nenhum X8 Ultra encontrado. Conecte o dongle 8K ou o cabo USB-C e feche o app oficial.");
        return Connect(dev);
    }

    object? Connect(MouseDevice dev)
    {
        _dev?.Dispose();
        _dev = dev;
        _model = null;
        _dev.Log += Log;
        return Read();
    }

    object Read()
    {
        var dev = _dev ?? throw new InvalidOperationException("Nenhum mouse conectado.");
        _version = dev.ReadVersion();
        _battery = dev.ReadBattery();
        _model ??= dev.ReadModelId();
        int last = -1;
        var img = dev.ReadFlash(new SyncProgress(v => { if (v != last) { last = v; _post(JsonSerializer.Serialize(new { @event = "progress", value = v }, Json)); } }));
        var cfg = MouseConfig.FromImage(img);
        return State(cfg, img);
    }

    object State(MouseConfig cfg, FlashImage img) => new
    {
        device = new
        {
            description = _dev!.Description,
            simulated = _dev.Description.StartsWith("Simulador"),
            battery = _battery is null ? null : new { percent = _battery.Percent, millivolts = _battery.Millivolts },
            firmware = _version is ushort v ? $"{v >> 8}.{v & 0xFF:D2}" : null,
            model = _model is { } md ? ModelIndex(md.Mid) : null,
        },
        config = ConfigDto.From(cfg),
        warnings = cfg.Warnings,
        hex = HexDump(img),
    };

    object Preview(ConfigDto dto)
    {
        var img = _dev?.Image ?? throw new InvalidOperationException("Leia o mouse antes de aplicar.");
        var cfg = MouseConfig.FromImage(img); dto.ApplyTo(cfg);
        return MouseConfig.Diff(img, cfg.ApplyTo(img)).Select(o => new { address = $"0x{o.Address:X4}", bytes = Packet.Hex(o.Bytes) });
    }

    object Apply(ConfigDto dto)
    {
        var dev = _dev ?? throw new InvalidOperationException("Nenhum mouse conectado.");
        var cfg = MouseConfig.FromImage(dev.Image!); dto.ApplyTo(cfg);
        int n = dev.Apply(cfg, _backupDir);
        Log($"{n} escrita(s) confirmadas. Backup em {_backupDir}");
        var state = Read();
        return new { count = n, state };
    }

    object? ImportBackup()
    {
        var path = _onUi(() =>
        {
            using var dlg = new OpenFileDialog { Filter = "Backup SharkConfig|*.json", InitialDirectory = _backupDir };
            return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
        });
        return path is null ? null : ConfigDto.From(MouseConfig.FromImage(MouseDevice.LoadBackup(path)));
    }

    static string HexDump(FlashImage img)
    {
        var sb = new StringBuilder();
        foreach (var (start, len) in Flash.ReadRegions)
            for (int a = start; a < start + len; a += 16)
                sb.AppendLine($"{a:X4}  {Packet.Hex(img.Data.AsSpan(a, Math.Min(16, start + len - a)))}");
        return sb.ToString();
    }

    void Log(string text) => _post(JsonSerializer.Serialize(new { @event = "log", text, time = DateTime.Now.ToString("HH:mm:ss") }, Json));

    sealed class SyncProgress(Action<int> a) : IProgress<int> { public void Report(int v) => a(v); }
}
