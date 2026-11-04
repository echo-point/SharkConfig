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

    /// <summary>Envia um pedido e espera a resposta com o mesmo opcode (3 tentativas, 100 ms — @0x18000d820).</summary>
    public byte[]? Transact(Cmd cmd, int address = 0, ReadOnlySpan<byte> data = default, int? length = null)
    {
        var req = Packet.Build(cmd, address, data, length);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Log?.Invoke("→ " + Packet.Hex(req));
            _t.Write(req);
            var deadline = Environment.TickCount64 + 100;
            while (Environment.TickCount64 < deadline)
            {
                var r = _t.Read((int)Math.Max(1, deadline - Environment.TickCount64));
                if (r is null) break;
                if (Packet.IsResponseTo(r, cmd)) { Log?.Invoke("← " + Packet.Hex(r)); return r; }
                Log?.Invoke("  (ignorado) " + Packet.Hex(r));
            }
        }
        Log?.Invoke($"✗ sem resposta para 0x{(byte)cmd:X2}");
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

    /// <summary>Grava somente os campos alterados. Faz backup JSON da imagem antes, na 1ª escrita.</summary>
    public int Apply(MouseConfig cfg, string backupDir)
    {
        if (Image is null) throw new InvalidOperationException("Leia a configuração antes de gravar.");
        var after = cfg.ApplyTo(Image);
        var ops = MouseConfig.Diff(Image, after);
        if (ops.Count == 0) return 0;
        SaveBackup(Image, backupDir);
        foreach (var (addr, bytes) in ops)
        {
            if (!Flash.IsWritable(addr, bytes.Length))
                throw new InvalidOperationException($"Escrita fora das faixas permitidas: 0x{addr:X4}+{bytes.Length}");
            if (Transact(Cmd.WriteFlash, addr, bytes) is null)
                throw new IOException($"Mouse não confirmou a escrita em 0x{addr:X4}");
            Image.Load(addr, bytes);
        }
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
