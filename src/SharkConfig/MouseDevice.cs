using System.Text.Json;
using SharkConfig.Protocol;
using SharkConfig.Transport;

namespace SharkConfig;

public sealed record BatteryInfo(int Percent, int Status, int Millivolts);

/// <summary>Operações de alto nível sobre o transporte, com a mesma política de retry do DLL oficial.</summary>
public sealed class MouseDevice : IDisposable
{
    readonly IHidTransport _t;
    public event Action<string>? Log;
    public FlashImage? Image { get; private set; }
    public string Description => _t.Description;

    public MouseDevice(IHidTransport t) => _t = t;

    public static MouseDevice? TryOpenReal(out string message)
    {
        var all = WinHid.Enumerate(WinHid.AttackSharkVid);
        var pick = WinHid.FindMouse(all);
        if (pick is null)
        {
            message = all.Count == 0
                ? "Nenhum dispositivo Attack Shark (VID 3554) encontrado. Conecte o dongle 8K ou o cabo."
                : "Dispositivo VID 3554 encontrado, mas sem coleção HID de 17 bytes:\n" + string.Join("\n", all);
            return null;
        }
        message = $"OUT: {pick.Value.Out}\nIN:  {pick.Value.In}";
        return new MouseDevice(new WinHidTransport(pick.Value.Out, pick.Value.In));
    }

    // O thread oficial espera ≥ 8 ms entre pacotes (@0x180014bc7); usamos 10 ms.
    const int MinGapMs = 10;
    long _lastSend;

    /// <summary>
    /// Envia um pedido e espera a resposta (3 tentativas, 100 ms — @0x18000d820).
    /// Leitura: checksum ok e mesmo opcode. Escrita (0x07): a resposta tem de ser o eco
    /// idêntico dos 17 bytes do pedido, como o oficial confere (@0x180014260).
    /// </summary>
    public byte[]? Transact(Cmd cmd, int address = 0, ReadOnlySpan<byte> data = default, int? length = null)
    {
        var req = Packet.Build(cmd, address, data, length);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            long wait = _lastSend + MinGapMs - Environment.TickCount64;
            if (wait > 0) Thread.Sleep((int)wait);
            Log?.Invoke("→ " + Packet.Hex(req));
            _t.Write(req);
            _lastSend = Environment.TickCount64;
            var deadline = Environment.TickCount64 + 100;
            while (Environment.TickCount64 < deadline)
            {
                var r = _t.Read((int)Math.Max(1, deadline - Environment.TickCount64));
                if (r is null) break;
                bool ok = cmd == Cmd.WriteFlash
                    ? r.Length >= Packet.Size && r.AsSpan(0, Packet.Size).SequenceEqual(req)
                    : Packet.IsResponseTo(r, cmd);
                if (ok) { Log?.Invoke("← " + Packet.Hex(r)); return r; }
                Log?.Invoke("  (ignorado) " + Packet.Hex(r));
            }
        }
        Log?.Invoke($"✗ sem resposta válida para 0x{(byte)cmd:X2}");
        return null;
    }

    public ushort? ReadVersion() => Transact(Cmd.Version) is { } r ? (ushort)(r[6] << 8 | r[7]) : null;

    public BatteryInfo? ReadBattery() =>
        Transact(Cmd.Battery) is { } r ? new BatteryInfo(r[6], r[7], r[8] << 8 | r[9]) : null;

    /// <summary>CID/MID do modelo. Pedido usa len=0x83 como o oficial; resposta [6]=CID [7]=MID [I].</summary>
    public (byte Cid, byte Mid)? ReadModelId() => Transact(Cmd.ReadCidMid, 0, default, 0x83) is { } r ? (r[6], r[7]) : null;

    public bool? ReadOnline() => Transact(Cmd.Online) is { } r ? r[6] != 0 : null;

    public FlashImage ReadFlash(IProgress<int>? progress = null)
    {
        var img = new FlashImage();
        int total = Flash.ReadRegions.Sum(r => r.Length), done = 0;
        foreach (var (start, length) in Flash.ReadRegions)
            for (int off = 0; off < length; off += Packet.MaxData)
            {
                int n = Math.Min(Packet.MaxData, length - off);
                var r = Transact(Cmd.ReadFlash, start + off, default, n)
                        ?? throw new IOException($"Falha lendo flash em 0x{start + off:X4}");
                img.Load(start + off, r.AsSpan(6, n));
                done += n;
                progress?.Report(done * 100 / total);
            }
        Image = img;
        return img;
    }

    /// <summary>
    /// Grava somente os campos alterados, do jeito do app oficial (1 pacote por campo, só 0x07).
    /// Proteções: recusa se a leitura do mouse veio com campos inválidos; confere todos os pacotes
    /// contra as faixas permitidas antes de enviar o primeiro; backup antes; eco exato de cada
    /// escrita; e no fim relê cada região gravada e compara byte a byte.
    /// </summary>
    public int Apply(MouseConfig cfg, string backupDir)
    {
        if (Image is null) throw new InvalidOperationException("Leia a configuração antes de gravar.");
        var current = MouseConfig.FromImage(Image);
        if (current.Warnings.Count > 0)
            throw new InvalidOperationException("A leitura do mouse tem campos inválidos (" + string.Join("; ", current.Warnings) +
                "). Por segurança, nada foi gravado. Leia o mouse de novo.");

        var after = cfg.ApplyTo(Image);
        var ops = MouseConfig.Diff(Image, after);
        if (ops.Count == 0) return 0;
        foreach (var (addr, bytes) in ops)
            if (!Flash.IsWritable(addr, bytes.Length))
                throw new InvalidOperationException($"Escrita fora das faixas permitidas: 0x{addr:X4}+{bytes.Length}. Nada foi gravado.");

        SaveBackup(Image, backupDir);
        foreach (var (addr, bytes) in ops)
        {
            if (Transact(Cmd.WriteFlash, addr, bytes) is null)
                throw new IOException($"O mouse não confirmou a escrita em 0x{addr:X4}. Os campos anteriores já foram gravados; " +
                    "leia o mouse de novo antes de continuar. O backup está em " + backupDir);
            Image.Load(addr, bytes);
        }

        // conferência: relê o que foi gravado
        foreach (var (addr, bytes) in ops)
        {
            var r = Transact(Cmd.ReadFlash, addr, default, bytes.Length)
                    ?? throw new IOException($"Não foi possível reler 0x{addr:X4} para conferir a gravação.");
            if (!r.AsSpan(6, bytes.Length).SequenceEqual(bytes))
                throw new IOException($"Conferência falhou em 0x{addr:X4}: gravado {Packet.Hex(bytes)}, lido {Packet.Hex(r.AsSpan(6, bytes.Length))}. " +
                    "O backup está em " + backupDir);
        }
        Log?.Invoke($"✓ {ops.Count} escrita(s) gravadas e conferidas");
        return ops.Count;
    }

    public static string SaveBackup(FlashImage img, string dir)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        var regions = Flash.ReadRegions.ToDictionary(r => $"0x{r.Start:X4}",
            r => Convert.ToHexString(img.Data, r.Start, r.Length));
        File.WriteAllText(path, JsonSerializer.Serialize(regions, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    public static FlashImage LoadBackup(string path)
    {
        var regions = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
        var img = new FlashImage();
        foreach (var (k, v) in regions) img.Load(Convert.ToInt32(k, 16), Convert.FromHexString(v));
        return img;
    }

    public void Dispose() => _t.Dispose();
}
