namespace SharkConfig.Protocol.Keyboard;

/// <summary>Iluminação principal (comando 0x87/0x07).</summary>
public sealed record LedSetting(byte Mode, byte SpeedRaw, byte Brightness, byte OptionByte, byte R, byte G, byte B)
{
    public const int MaxSpeed = 4;       // gravado como MaxSpeed − velocidade
    public const int MaxBrightness = 4;
    public const byte ColorCustom = 7;   // nibble baixo de OptionByte: cor RGB escolhida
    public const byte ColorRainbow = 8;  // "dazzle": cores do arco-íris

    public int Speed => MaxSpeed - Math.Min(SpeedRaw, (byte)MaxSpeed);
    public int Direction => OptionByte >> 4;
    public int ColorMode => OptionByte & 0x0F;   // 0–6 = cor predefinida, 7 = RGB, 8 = arco-íris
}

/// <summary>Efeitos de luz do X68 HE: índice gravado, nome e direções (lightLayout do app oficial).</summary>
public sealed record LightEffect(byte Mode, string Label, string[] Directions, bool HasSpeed, bool HasColor);

/// <summary>Configuração do teclado lida e gravada pelo SharkConfig.</summary>
public sealed class KeyboardConfig
{
    public const int Slots = KeyboardModel.SlotCount;

    /// <summary>Código gravado no byte [2] do comando 0x03/0x83.</summary>
    public static readonly (byte Code, int Hz)[] ReportRates =
        [(6, 125), (5, 250), (4, 500), (3, 1000), (2, 2000), (1, 4000), (0, 8000)];

    public static readonly LightEffect[] Effects =
    [
        new(0, "Desligada", [], false, false),
        new(1, "Fixa", [], false, true),
        new(2, "Respiração", [], true, true),
        new(3, "Neon", [], true, false),
        new(4, "Onda", ["Para a direita", "Para a esquerda", "Para baixo", "Para cima"], true, true),
        new(5, "Ondulação", [], true, true),
        new(6, "Gotas de chuva", [], true, true),
        new(7, "Serpente", ["Zigue-zague", "Espiral"], true, true),
        new(8, "Reação ao toque", [], true, true),
        new(9, "Convergência", [], true, true),
        new(10, "Senoide", [], true, true),
        new(11, "Caleidoscópio", ["Para fora", "Para dentro"], true, true),
        new(12, "Onda em linha", ["Para a direita", "Para a esquerda"], true, true),
        new(14, "Laser", [], true, true),
        new(15, "Onda circular", ["Anti-horário", "Horário"], true, true),
        new(16, "Cintilante", [], true, true),
        new(17, "Chuva", [], true, true),
        new(18, "Meteoro", [], true, true),
        new(19, "Apaga ao toque", [], true, true),
    ];

    public byte ReportRateCode { get; set; }
    public LedSetting Led { get; set; } = new(0, 0, 0, 0, 0, 0, 0);

    // Dados por posição da matriz (comando 0xE5). Valores de curso em unidades do firmware
    // (mm × StepMultiplier).
    public byte[] Mode { get; set; } = new byte[Slots];
    public ushort[] Travel { get; set; } = new ushort[Slots];
    public ushort[] LiftTravel { get; set; } = new ushort[Slots];
    public ushort[] RtPress { get; set; } = new ushort[Slots];
    public ushort[] RtLift { get; set; } = new ushort[Slots];
    public ushort[] DeadZone { get; set; } = new ushort[Slots];

    public List<string> Warnings { get; } = [];

    public KeyboardConfig Clone() => new()
    {
        ReportRateCode = ReportRateCode, Led = Led,
        Mode = (byte[])Mode.Clone(), Travel = (ushort[])Travel.Clone(), LiftTravel = (ushort[])LiftTravel.Clone(),
        RtPress = (ushort[])RtPress.Clone(), RtLift = (ushort[])RtLift.Clone(), DeadZone = (ushort[])DeadZone.Clone(),
    };

    public static bool IsRapidTrigger(byte mode) => (mode & 0x80) != 0;
    /// <summary>Só teclas no modo normal (sem DKS, Mod-Tap, Toggle ou SnapTap) são editadas.</summary>
    public static bool IsNormal(byte mode) => (mode & 0x7F) == 0;
}

/// <summary>Limites de curso em mm conforme a versão do firmware (regras do app oficial).</summary>
public sealed record TravelLimits(int StepMultiplier, double Step, double TravelMin, double TravelMax,
    double RtMin, double RtMax, double DeadZoneMin, double DeadZoneMax)
{
    /// <summary>
    /// Firmware &lt; 0x300: unidade 0,1 mm, curso até 4 mm. 0x300–0x4FF: 0,01 mm. ≥ 0x500: 0,005 mm.
    /// (get磁轴行程步进倍数 / defaultStepForCurrentDevice / 选中按键行程最大值 do app oficial.)
    /// </summary>
    public static TravelLimits For(int? version)
    {
        int v = version ?? 0;
        int mult = v >= 1280 ? 200 : v >= 768 ? 100 : 10;
        double step = v >= 1280 ? 0.005 : v >= 768 ? 0.01 : 0.1;
        double max = v is > 0 and < 768 ? 4.0 : 3.3;
        return new(mult, step, 0.1, max, Math.Max(0.01, step), 2.0, 0, 1.0);
    }

    public ushort ToRaw(double mm) => (ushort)Math.Round(mm * StepMultiplier);
    public double ToMm(int raw) => Math.Round((double)raw / StepMultiplier, 3);
}
