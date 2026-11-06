using System.Text.Json;
using SharkConfig.Protocol.Keyboard;
using SharkConfig.Transport;

namespace SharkConfig;

public sealed record KeyboardInfo(uint Id, int UsbVersion, int? RfVersion);

/// <summary>
/// Teclados HE da Attack Shark (plataforma RY5088), pelo cabo. Mesma cadência do app oficial:
/// 10 ms antes de cada pacote, 10 ms entre o pedido e a leitura da resposta, 100 ms depois de gravar.
/// </summary>
public sealed class KeyboardDevice : IDisposable
{
    readonly IFeatureTransport _t;
    public event Action<string>? Log;
    public string Description => _t.Description;
    public string? DevicePath { get; private init; }
    public KeyboardInfo? Info { get; private set; }
    public KeyboardModel? Model { get; private set; }
    public KeyboardConfig? Config { get; private set; }
    public TravelLimits Limits => TravelLimits.For(Info?.RfVersion ?? Info?.UsbVersion);

    public KeyboardDevice(IFeatureTransport t) => _t = t;

    public static KeyboardDevice? TryOpenReal(out string message)
    {
        var all = WinHid.Enumerate(WinHid.KeyboardVid);
        var pick = WinHid.FindKeyboard(all);
        if (pick is null)
        {
            message = all.Count == 0
                ? "Nenhum teclado Attack Shark (VID 3151) encontrado."
                : "Teclado VID 3151 encontrado, mas sem a interface de configuração FFFF:02:\n" + string.Join("\n", all);
            return null;
        }
        message = "KB:  " + pick;
        return new KeyboardDevice(new WinFeatureTransport(pick)) { DevicePath = pick.Path };
    }

    const int GapMs = 10, ReadDelayMs = 10, WriteSleepMs = 100;
    long _lastSend;

    void Send(ReadOnlySpan<byte> payload, KbChecksum mode)
    {
        var p = KbPacket.Build(payload, mode);
        long wait = _lastSend + GapMs - Environment.TickCount64;
        if (wait > 0) Thread.Sleep((int)wait);
        Log?.Invoke("→ " + KbPacket.Hex(p.AsSpan(0, Math.Min(16, p.Length))) + (p.Length > 16 ? " …" : ""));
        if (!_t.SetFeature(p)) throw new IOException($"O teclado recusou o pacote 0x{payload[0]:X2}.");
        _lastSend = Environment.TickCount64;
    }

    /// <summary>Pedido + resposta (3 tentativas). <paramref name="valid"/> confere a resposta.</summary>
    byte[]? Query(ReadOnlySpan<byte> payload, Func<byte[], bool>? valid = null, KbChecksum mode = KbChecksum.Bit7)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Send(payload, mode);
            Thread.Sleep(ReadDelayMs);
            var r = _t.GetFeature();
            if (r is not null && (valid?.Invoke(r) ?? true))
            {
                Log?.Invoke("← " + KbPacket.Hex(r.AsSpan(0, 16)) + " …");
                return r;
            }
            if (r is not null) Log?.Invoke("  (ignorado) " + KbPacket.Hex(r.AsSpan(0, 16)) + " …");
        }
        Log?.Invoke($"✗ sem resposta válida para 0x{payload[0]:X2}");
        return null;
    }

    static Func<byte[], bool> Echo(KbCmd c) => r => r[0] == (byte)c;

    public KeyboardInfo ReadInfo()
    {
        var r = Query([(byte)KbCmd.GetDeviceInfo], Echo(KbCmd.GetDeviceInfo))
                ?? throw new IOException("O teclado não respondeu ao pedido de identificação (0x8F).");
        uint id = BitConverter.ToUInt32(r, 1);
        int usb = r[8] << 8 | r[7];
        int? rf = null;
        if (Query([(byte)KbCmd.GetRfVersion]) is { } rr && (rr[2] << 8 | rr[1]) is var v && v != 0 && rr[0] == (byte)KbCmd.GetRfVersion) rf = v;
        Info = new KeyboardInfo(id, usb, rf);
        Model = KeyboardModels.Find(id);
        return Info;
    }

    public byte? ReadReportRate() => Query([(byte)KbCmd.GetReportRate], Echo(KbCmd.GetReportRate)) is { } r ? r[2] : null;

    public LedSetting? ReadLed() =>
        Query([(byte)KbCmd.GetLed], Echo(KbCmd.GetLed)) is { } r ? new LedSetting(r[1], r[2], r[3], r[4], r[5], r[6], r[7]) : null;

    /// <summary>Lê uma tabela por tecla (0xE5 op, 1, página): cada página devolve 64 bytes de dados.</summary>
    public byte[] ReadMagnetism(MagOp op, int pages)
    {
        var all = new byte[pages * KbPacket.Size];
        for (int page = 0; page < pages; page++)
        {
            var r = Query([(byte)KbCmd.GetMagnetism, (byte)op, 1, (byte)page])
                    ?? throw new IOException($"Falha lendo a tabela 0xE5/{(byte)op} página {page}.");
            r.CopyTo(all, page * KbPacket.Size);
        }
        return all;
    }

    static ushort[] U16(byte[] b) => Enumerable.Range(0, KeyboardConfig.Slots).Select(i => (ushort)(b[2 * i] | b[2 * i + 1] << 8)).ToArray();

    public KeyboardConfig ReadAll(IProgress<int>? progress = null)
    {
        if (Info is null) ReadInfo();
        var c = new KeyboardConfig();
        void P(int v) => progress?.Report(v);
        c.ReportRateCode = ReadReportRate() ?? throw new IOException("O teclado não informou a taxa de atualização.");
        P(5);
        c.Led = ReadLed() ?? throw new IOException("O teclado não informou a iluminação.");
        P(10);
        c.Mode = ReadMagnetism(MagOp.Mode, 2)[..KeyboardConfig.Slots]; P(20);
        c.Travel = U16(ReadMagnetism(MagOp.Travel, 4)); P(36);
        c.LiftTravel = U16(ReadMagnetism(MagOp.LiftTravel, 4)); P(52);
        c.RtPress = U16(ReadMagnetism(MagOp.RtPress, 4)); P(68);
        c.RtLift = U16(ReadMagnetism(MagOp.RtLift, 4)); P(84);
        c.DeadZone = U16(ReadMagnetism(MagOp.DeadZone, 4)); P(100);
        Validate(c);
        Config = c;
        return c;
    }

    /// <summary>Confere se a leitura faz sentido antes de permitir gravar (protege contra formato errado).</summary>
    void Validate(KeyboardConfig c)
    {
        if (!KeyboardConfig.ReportRates.Any(r => r.Code == c.ReportRateCode)) c.Warnings.Add($"taxa de atualização desconhecida ({c.ReportRateCode})");
        if (c.Led.Mode > 25) c.Warnings.Add($"efeito de luz desconhecido ({c.Led.Mode})");
        if (Model is null) return;
        var lim = Limits;
        ushort max = lim.ToRaw(4.0);
        foreach (var k in Model.Keys)
        {
            if (Model.SlotOf(k) is not int s) continue;
            if (c.Travel[s] == 0 || c.Travel[s] > max) c.Warnings.Add($"curso inválido em {k.Code} ({c.Travel[s]})");
            if (c.LiftTravel[s] > max || c.RtPress[s] > max || c.RtLift[s] > max || c.DeadZone[s] > max)
                c.Warnings.Add($"valores de curso fora da faixa em {k.Code}");
        }
    }

    // ---------------- gravação ----------------

    public sealed record WriteOp(string Label, byte[][] Packets);

    /// <summary>
    /// Pacotes para levar <see cref="Config"/> até <paramref name="next"/>: só o que mudou, no formato
    /// do app oficial (setReportRate, setLightSetting e setMultiMagnetismInfo, tabelas inteiras em
    /// blocos de 56 bytes). O último bloco da última tabela leva a marca de fim [4]=1.
    /// </summary>
    public List<WriteOp> Plan(KeyboardConfig next)
    {
        var cur = Config ?? throw new InvalidOperationException("Leia o teclado antes de gravar.");
        var ops = new List<WriteOp>();
        if (next.ReportRateCode != cur.ReportRateCode)
            ops.Add(new("taxa de atualização", [KbPacket.Build([(byte)KbCmd.SetReportRate, 0, next.ReportRateCode], KbChecksum.Bit7)]));
        if (next.Led != cur.Led)
        {
            var l = next.Led;
            ops.Add(new("iluminação", [KbPacket.Build([(byte)KbCmd.SetLed, l.Mode, l.SpeedRaw, l.Brightness, l.OptionByte, l.R, l.G, l.B], KbChecksum.Bit8)]));
        }

        var tables = new List<(MagOp Op, byte[] Data)>();
        if (!next.Mode.SequenceEqual(cur.Mode)) tables.Add((MagOp.Mode, next.Mode));
        void T(MagOp op, ushort[] a, ushort[] b) { if (!a.SequenceEqual(b)) tables.Add((op, a.SelectMany(v => new[] { (byte)v, (byte)(v >> 8) }).ToArray())); }
        T(MagOp.Travel, next.Travel, cur.Travel);
        T(MagOp.LiftTravel, next.LiftTravel, cur.LiftTravel);
        T(MagOp.RtPress, next.RtPress, cur.RtPress);
        T(MagOp.RtLift, next.RtLift, cur.RtLift);
        T(MagOp.DeadZone, next.DeadZone, cur.DeadZone);
        for (int ti = 0; ti < tables.Count; ti++)
        {
            var (op, data) = tables[ti];
            int pages = (data.Length + KbPacket.PageData - 1) / KbPacket.PageData;
            var pk = new byte[pages][];
            for (int p = 0; p < pages; p++)
            {
                var payload = new byte[8 + KbPacket.PageData];
                payload[0] = (byte)KbCmd.SetMagnetism; payload[1] = (byte)op; payload[2] = 1; payload[3] = (byte)p;
                payload[4] = (byte)(ti == tables.Count - 1 && p == pages - 1 ? 1 : 0);
                data.AsSpan(p * KbPacket.PageData, Math.Min(KbPacket.PageData, data.Length - p * KbPacket.PageData)).CopyTo(payload.AsSpan(8));
                pk[p] = KbPacket.Build(payload, KbChecksum.Bit7);
            }
            ops.Add(new($"tabela de teclas {op}", pk));
        }
        return ops;
    }

    public int Apply(KeyboardConfig next, string backupDir)
    {
        var cur = Config ?? throw new InvalidOperationException("Leia o teclado antes de gravar.");
        if (cur.Warnings.Count > 0)
            throw new InvalidOperationException("A leitura do teclado tem campos inválidos (" + string.Join("; ", cur.Warnings.Take(4)) +
                "). Por segurança, nada foi gravado.");
        if (Model is null) throw new InvalidOperationException("Modelo de teclado não suportado para gravação.");
        var plan = Plan(next);
        if (plan.Count == 0) return 0;
        SaveBackup(backupDir);
        int n = 0;
        foreach (var op in plan)
        {
            foreach (var p in op.Packets) { Send(p, KbChecksum.None); n++; }
            Thread.Sleep(op.Label.StartsWith("tabela") ? 20 : WriteSleepMs);
        }
        if (plan.Any(o => o.Label.StartsWith("tabela"))) Thread.Sleep(500);

        // conferência: relê e compara com o que foi pedido
        var after = ReadAll();
        var diffs = new List<string>();
        if (after.ReportRateCode != next.ReportRateCode) diffs.Add("taxa de atualização");
        if (after.Led != next.Led) diffs.Add("iluminação");
        if (!after.Mode.SequenceEqual(next.Mode)) diffs.Add("modo das teclas");
        if (!after.Travel.SequenceEqual(next.Travel)) diffs.Add("acionamento");
        if (!after.LiftTravel.SequenceEqual(next.LiftTravel)) diffs.Add("soltura");
        if (!after.RtPress.SequenceEqual(next.RtPress) || !after.RtLift.SequenceEqual(next.RtLift)) diffs.Add("Rapid Trigger");
        if (!after.DeadZone.SequenceEqual(next.DeadZone)) diffs.Add("zona morta");
        if (diffs.Count > 0)
            throw new IOException("Conferência falhou em: " + string.Join(", ", diffs) + ". O backup está em " + backupDir);
        Log?.Invoke($"✓ {n} pacote(s) gravados e conferidos");
        return n;
    }

    public string SaveBackup(string dir)
    {
        var c = Config ?? throw new InvalidOperationException("Leia o teclado antes de exportar.");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"teclado-{Info?.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            device = Info, c.ReportRateCode, c.Led, c.Mode, c.Travel, c.LiftTravel, c.RtPress, c.RtLift, c.DeadZone,
        }, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    public static KeyboardConfig LoadBackup(string path)
    {
        var o = JsonSerializer.Deserialize<KeyboardConfig>(File.ReadAllText(path)) ?? throw new IOException("backup inválido");
        if (o.Mode.Length != KeyboardConfig.Slots || o.Travel.Length != KeyboardConfig.Slots) throw new IOException("backup de outro formato");
        return o;
    }

    /// <summary>Despejo de leitura para o modo --kb-probe (não grava nada).</summary>
    public void Probe(TextWriter w)
    {
        var info = ReadInfo();
        w.WriteLine($"id={info.Id} usb=0x{info.UsbVersion:X4} rf={(info.RfVersion is int rf ? $"0x{rf:X4}" : "-")} modelo={Model?.Name} {Model?.Revision}");
        foreach (var (cmd, name) in new[] { (KbCmd.GetReportRate, "report"), (KbCmd.GetLed, "led"), (KbCmd.GetKbOption, "kboption"), (KbCmd.GetProfile, "profile"), (KbCmd.GetDebounce, "debounce"), (KbCmd.GetSleepTime, "sleep") })
            w.WriteLine($"{name,-9} {(Query([(byte)cmd]) is { } r ? KbPacket.Hex(r.AsSpan(0, 24)) : "sem resposta")}");
        foreach (var (op, pages) in new[] { (MagOp.Mode, 2), (MagOp.Travel, 4), (MagOp.LiftTravel, 4), (MagOp.RtPress, 4), (MagOp.RtLift, 4), (MagOp.DeadZone, 4) })
        {
            var d = ReadMagnetism(op, pages);
            w.WriteLine($"E5/{op}:");
            for (int i = 0; i < d.Length; i += 32) w.WriteLine("   " + KbPacket.Hex(d.AsSpan(i, 32)));
        }
    }

    public void Dispose() => _t.Dispose();
}
