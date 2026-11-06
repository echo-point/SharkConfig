namespace SharkConfig.Protocol.Keyboard;

/// <summary>
/// Opcodes dos teclados RY5088 (byte [0] do feature report). Fonte: classe base dos modelos
/// "ry5088_*" do Attack Shark Driver v4 — ver docs/PROTOCOL-KEYBOARD.md.
/// Somente os listados em <see cref="KbPacket.AllowedCommands"/> podem ser enviados.
/// </summary>
public enum KbCmd : byte
{
    SetReportRate = 0x03,
    SetLed = 0x07,
    SetKbOption = 0x09,
    SetMagnetism = 0x65,

    GetRfVersion = 0x80,
    GetReportRate = 0x83,
    GetProfile = 0x84,
    GetLedOnOff = 0x85,
    GetDebounce = 0x86,
    GetLed = 0x87,
    GetSideLed = 0x88,
    GetKbOption = 0x89,
    GetKeyMatrix = 0x8A,
    GetDeviceInfo = 0x8F,   // id do modelo (u32 em [1..4]) e versão USB ([7..8])
    GetSleepTime = 0x91,
    GetMagnetism = 0xE5,
    // Nunca enviados (documentados só para referência):
    // Reset=0x01 (volta ao padrão de fábrica), SetProfile=0x04, SetDebounce=0x06, SetSideLed=0x08,
    // SetKeyMatrix=0x0A, SetMacro=0x0B, SetUserPic=0x0C, SetFn=0x10, SetSleep=0x11, SetUserGif=0x12,
    // SetMagnetismReport=0x1B, Calibração=0x1C/0x1E, telas/OLED=0x22/0x25/0x27–0x2A/0x30–0x32,
    // SetSku=0x50, FlashChipErase=0xAC, bootloader/atualização de firmware.
}

/// <summary>Onde vai o checksum do pacote (o app oficial chama de Bit7/Bit8).</summary>
public enum KbChecksum { Bit7, Bit8, None }

/// <summary>Operações do comando 0x65/0xE5 (dados por tecla, um item por posição da matriz).</summary>
public enum MagOp : byte
{
    Travel = 0,          // ponto de acionamento (u16 por tecla)
    LiftTravel = 1,      // ponto de soltura (u16)
    RtPress = 2,         // Rapid Trigger: sensibilidade ao pressionar (u16)
    RtLift = 3,          // Rapid Trigger: sensibilidade ao soltar (u16)
    Dynamic = 4,         // DKS (não usado)
    ModTapTime = 5,      // não usado
    DeadZone = 6,        // zona morta inferior (u16)
    Mode = 7,            // modo da tecla (u8): bits 0–6 = modo (0 normal), bit 7 = Rapid Trigger
    TopDeadZone = 251,   // zona morta superior (u8), firmware ≥ 0x0400
    SwitchType = 252,    // tipo de switch (u8)
    Calibration = 254,   // leitura de calibração (u16)
}

public static class KbPacket
{
    public const int Size = 64;
    public const int PageData = 56;   // escrita em blocos: 8 bytes de cabeçalho + 56 de dados

    public static readonly HashSet<KbCmd> AllowedCommands =
    [
        KbCmd.SetReportRate, KbCmd.SetLed, KbCmd.SetKbOption, KbCmd.SetMagnetism,
        KbCmd.GetRfVersion, KbCmd.GetReportRate, KbCmd.GetProfile, KbCmd.GetLedOnOff, KbCmd.GetDebounce,
        KbCmd.GetLed, KbCmd.GetSideLed, KbCmd.GetKbOption, KbCmd.GetKeyMatrix, KbCmd.GetDeviceInfo,
        KbCmd.GetSleepTime, KbCmd.GetMagnetism,
    ];

    /// <summary>Operações de 0x65 que o SharkConfig grava: curso, soltura, Rapid Trigger, zona morta e modo.</summary>
    public static readonly HashSet<MagOp> WritableMagOps =
        [MagOp.Travel, MagOp.LiftTravel, MagOp.RtPress, MagOp.RtLift, MagOp.DeadZone, MagOp.Mode];

    /// <summary>
    /// Monta o pacote como o sendFeature do app oficial: completa até 8 bytes e grava o checksum
    /// 255 − soma em [7] (Bit7: soma de [0..6]) ou [8] (Bit8: soma de [0..7]).
    /// </summary>
    public static byte[] Build(ReadOnlySpan<byte> payload, KbChecksum mode)
    {
        if (payload.Length == 0 || payload.Length > Size) throw new ArgumentException("pacote vazio ou maior que 64 bytes");
        var cmd = (KbCmd)payload[0];
        if (!AllowedCommands.Contains(cmd))
            throw new InvalidOperationException($"Comando 0x{payload[0]:X2} bloqueado pela política de segurança.");
        if (cmd == KbCmd.SetMagnetism && (payload.Length < 2 || !WritableMagOps.Contains((MagOp)payload[1])))
            throw new InvalidOperationException($"Operação 0x65/{(payload.Length > 1 ? payload[1] : 0)} bloqueada pela política de segurança.");
        var p = new byte[Size];
        payload.CopyTo(p);
        if (mode == KbChecksum.Bit7) p[7] = Checksum(p.AsSpan(0, 7));
        else if (mode == KbChecksum.Bit8) p[8] = Checksum(p.AsSpan(0, 8));
        return p;
    }

    public static byte Checksum(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        foreach (var b in data) sum += b;
        return (byte)(255 - (sum & 0xFF));
    }

    public static string Hex(ReadOnlySpan<byte> b) =>
        string.Join(" ", Convert.ToHexString(b).Chunk(2).Select(c => new string(c)));
}
