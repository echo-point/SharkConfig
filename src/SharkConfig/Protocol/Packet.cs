namespace SharkConfig.Protocol;

/// <summary>
/// Opcodes do protocolo (byte [1] do relatório). Fonte: HIDUsb.dll, ver docs/PROTOCOL.md.
/// Somente os listados em <see cref="Packet.AllowedCommands"/> podem ser enviados.
/// </summary>
public enum Cmd : byte
{
    ReadEncryption = 0x01,
    Online = 0x03,
    Battery = 0x04,
    WriteFlash = 0x07,
    ReadFlash = 0x08,
    Version = 0x12,
    ReadCidMid = 0xF1,   // só leitura: identifica o modelo (CID/MID) — @0x18000eaf0
    // Perigosos — nunca enviados pelo app (documentados aqui só para referência):
    // ClearSetting=0x09, SetVidPid=0x0B, SetDescriptor=0x0C, EnterDfu=0x0D,
    // EnterMtk=0x11, WriteCidMid=0xF0, Pair=0x05, SetDongleId=0x1A, SetSerial=0x1F
}

/// <summary>
/// Relatório de 17 bytes: [0]=0x08 [1]=cmd [2]=0 [3]=addrHi [4]=addrLo [5]=len
/// [6..15]=dados [16]=checksum (0x55 - soma de [0..15]).  (HIDUsb.dll @0x180010e30)
/// </summary>
public static class Packet
{
    public const int Size = 17;
    public const byte ReportId = 0x08;
    public const int MaxData = 10;

    public static readonly HashSet<Cmd> AllowedCommands =
        [Cmd.Online, Cmd.Battery, Cmd.WriteFlash, Cmd.ReadFlash, Cmd.Version, Cmd.ReadCidMid];

    public static byte Checksum(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        foreach (var b in data) sum += b;
        return (byte)(0x55 - sum);
    }

    public static byte[] Build(Cmd cmd, int address = 0, ReadOnlySpan<byte> data = default, int? length = null)
    {
        if (!AllowedCommands.Contains(cmd))
            throw new InvalidOperationException($"Comando 0x{(byte)cmd:X2} bloqueado pela política de segurança.");
        if (data.Length > MaxData) throw new ArgumentException("máx. 10 bytes por pacote");
        var p = new byte[Size];
        p[0] = ReportId;
        p[1] = (byte)cmd;
        p[2] = 0;
        p[3] = (byte)(address >> 8);
        p[4] = (byte)address;
        p[5] = (byte)(length ?? data.Length);   // modo mouse: sem bit 0x80
        data.CopyTo(p.AsSpan(6));
        p[16] = Checksum(p.AsSpan(0, 16));
        return p;
    }

    /// <summary>Mesmo critério do DLL (@0x18000d940): checksum ok e eco do opcode.</summary>
    public static bool IsResponseTo(ReadOnlySpan<byte> resp, Cmd cmd) =>
        resp.Length >= Size && resp[1] == (byte)cmd && resp[16] == Checksum(resp[..16]);

    public static string Hex(ReadOnlySpan<byte> b) =>
        string.Join(" ", Convert.ToHexString(b).Chunk(2).Select(c => new string(c)));
}
