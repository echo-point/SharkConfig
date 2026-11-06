using System.Text;
using SharkConfig.Protocol.Keyboard;
using SharkConfig.Transport;

namespace SharkConfig.UI;

/// <summary>
/// Configuração do teclado no formato da interface. As listas por tecla seguem a ordem de
/// <see cref="KeyboardModel.Keys"/>; cursos em mm.
/// </summary>
public sealed class KbConfigDto
{
    public int ReportRate { get; set; }
    public int LedMode { get; set; }
    public int LedSpeed { get; set; }
    public int LedBrightness { get; set; }
    public int LedDirection { get; set; }
    public bool LedRainbow { get; set; }
    public string LedColor { get; set; } = "#FFFFFF";
    public bool[] Rt { get; set; } = [];
    public double[] Travel { get; set; } = [];
    public double[] Lift { get; set; } = [];
    public double[] RtPress { get; set; } = [];
    public double[] RtLift { get; set; } = [];
    public double[] DeadZone { get; set; } = [];

    public static KbConfigDto From(KeyboardConfig c, KeyboardModel m, TravelLimits lim)
    {
        int[] slots = m.Keys.Select(k => m.SlotOf(k) ?? -1).ToArray();
        double Mm(ushort[] a, int s) => s < 0 ? 0 : lim.ToMm(a[s]);
        var l = c.Led;
        return new()
        {
            ReportRate = c.ReportRateCode,
            LedMode = l.Mode, LedSpeed = l.Speed, LedBrightness = l.Brightness, LedDirection = l.Direction,
            LedRainbow = l.ColorMode == LedSetting.ColorRainbow,
            // o app oficial grava o branco como #FAFFFA e mostra #FAFFFA como branco
            LedColor = (l.R, l.G, l.B) == (0xFA, 0xFF, 0xFA) ? "#FFFFFF" : $"#{l.R:X2}{l.G:X2}{l.B:X2}",
            Rt = slots.Select(s => s >= 0 && KeyboardConfig.IsRapidTrigger(c.Mode[s])).ToArray(),
            Travel = slots.Select(s => Mm(c.Travel, s)).ToArray(),
            Lift = slots.Select(s => Mm(c.LiftTravel, s)).ToArray(),
            RtPress = slots.Select(s => Mm(c.RtPress, s)).ToArray(),
            RtLift = slots.Select(s => Mm(c.RtLift, s)).ToArray(),
            DeadZone = slots.Select(s => Mm(c.DeadZone, s)).ToArray(),
        };
    }

    /// <summary>
    /// Leva a edição da interface para uma cópia da config lida. Só o que o usuário mudou é
    /// convertido (com limite e passo); o resto fica byte a byte como veio do teclado.
    /// </summary>
    public KeyboardConfig ApplyTo(KeyboardConfig read, KeyboardModel m, TravelLimits lim)
    {
        var before = From(read, m, lim);
        var c = read.Clone();
        if (ReportRate != before.ReportRate)
            c.ReportRateCode = KeyboardConfig.ReportRates.Any(r => r.Code == ReportRate)
                ? (byte)ReportRate : throw new ArgumentException($"Taxa de atualização: código {ReportRate} não é aceito.");

        if (LedMode != before.LedMode || LedSpeed != before.LedSpeed || LedBrightness != before.LedBrightness ||
            LedDirection != before.LedDirection || LedRainbow != before.LedRainbow || !LedColor.Equals(before.LedColor, StringComparison.OrdinalIgnoreCase))
        {
            var fx = KeyboardConfig.Effects.FirstOrDefault(e => e.Mode == LedMode)
                     ?? throw new ArgumentException($"Efeito de luz {LedMode} não é aceito.");
            int dir = fx.Directions.Length == 0 ? 0 : Math.Clamp(LedDirection, 0, fx.Directions.Length - 1);
            var rgb = System.Drawing.ColorTranslator.FromHtml(LedColor);
            if ((rgb.R, rgb.G, rgb.B) == (0xFF, 0xFF, 0xFF)) rgb = System.Drawing.Color.FromArgb(0xFA, 0xFF, 0xFA);
            byte colorMode = LedRainbow ? LedSetting.ColorRainbow : LedSetting.ColorCustom;
            c.Led = new LedSetting((byte)LedMode, (byte)(LedSetting.MaxSpeed - Math.Clamp(LedSpeed, 0, LedSetting.MaxSpeed)),
                (byte)Math.Clamp(LedBrightness, 0, LedSetting.MaxBrightness), (byte)(dir << 4 | colorMode), rgb.R, rgb.G, rgb.B);
        }

        double Snap(double v, double min, double max) => Math.Clamp(Math.Round(v / lim.Step) * lim.Step, min, max);
        for (int i = 0; i < m.Keys.Length; i++)
        {
            if (m.SlotOf(m.Keys[i]) is not int s) continue;
            bool touched = false;
            void Set(double[] edited, double[] orig, ushort[] dst, double min, double max)
            {
                if (i >= edited.Length || Math.Abs(edited[i] - orig[i]) < 1e-9) return;
                dst[s] = lim.ToRaw(Snap(edited[i], min, max));
                touched = true;
            }
            Set(Travel, before.Travel, c.Travel, lim.TravelMin, lim.TravelMax);
            Set(Lift, before.Lift, c.LiftTravel, lim.TravelMin, lim.TravelMax);
            Set(RtPress, before.RtPress, c.RtPress, lim.RtMin, lim.RtMax);
            Set(RtLift, before.RtLift, c.RtLift, lim.RtMin, lim.RtMax);
            Set(DeadZone, before.DeadZone, c.DeadZone, lim.DeadZoneMin, lim.DeadZoneMax);
            if (i < Rt.Length && Rt[i] != before.Rt[i]) { c.Mode[s] = (byte)(Rt[i] ? 0x80 : 0x00); touched = true; }
            if (touched && !KeyboardConfig.IsNormal(read.Mode[s]))
                throw new ArgumentException($"A tecla {m.Keys[i].Code} usa um modo avançado (DKS, Mod-Tap, Toggle ou SnapTap) " +
                    "configurado no app oficial; o SharkConfig não altera essas teclas.");
        }
        return c;
    }
}

public sealed partial class Bridge
{
    KeyboardDevice? _kb;

    object KbInit() => new
    {
        reportRates = KeyboardConfig.ReportRates.Select(r => new { code = r.Code, hz = r.Hz }),
        effects = KeyboardConfig.Effects.Select(e => new { mode = e.Mode, label = e.Label, directions = e.Directions, speed = e.HasSpeed, color = e.HasColor }),
        led = new { maxSpeed = LedSetting.MaxSpeed, maxBrightness = LedSetting.MaxBrightness },
        // desenho da tela inicial, antes de qualquer teclado ser aberto
        layout = KeyboardModels.All[^1] is var m ? new
        {
            width = m.LayoutW, height = m.LayoutH,
            frame = KeyboardModels.FrameImage,
            plate = new { x = KeyboardModels.FramePlate.X, y = KeyboardModels.FramePlate.Y, w = KeyboardModels.FramePlate.W, h = KeyboardModels.FramePlate.H },
            keys = m.Keys.Select(k => new { code = k.Code, label = k.Label, x = k.X, y = k.Y, w = k.W, h = k.H, present = true, advanced = false }),
        } : null,
    };

    object? TryAutoConnectKeyboard()
    {
        if (_kb is not null) return KbRead();
        if (WinHid.FindKeyboard(WinHid.Enumerate(WinHid.KeyboardVid)) is null) return null;
        try
        {
            var kb = KeyboardDevice.TryOpenReal(out var message);
            if (kb is null) return null;
            Log(message);
            _kb = kb;
            _kb.Log += Log;
            return KbRead();
        }
        catch (Exception ex)
        {
            _kb?.Dispose(); _kb = null;
            throw new InvalidOperationException("O teclado foi encontrado, mas não respondeu. Feche o app oficial da Attack Shark e reconecte o teclado. (" + ex.Message + ")");
        }
    }

    object KbRead()
    {
        var kb = _kb ?? throw new InvalidOperationException("Nenhum teclado conectado.");
        kb.ReadInfo();
        int last = -1;
        var cfg = kb.ReadAll(new SyncProgress(v => { if (v != last) { last = v; _post(System.Text.Json.JsonSerializer.Serialize(new { @event = "progress", value = v }, Json)); } }));
        return KbState(kb, cfg);
    }

    object KbState(KeyboardDevice kb, KeyboardConfig cfg)
    {
        var m = kb.Model;
        var lim = kb.Limits;
        var info = kb.Info!;
        int[] slots = m?.Keys.Select(k => m.SlotOf(k) ?? -1).ToArray() ?? [];
        return new
        {
            kind = "keyboard",
            device = new
            {
                description = kb.Description,
                name = m?.Name ?? $"Teclado {info.Id}",
                revision = m?.Revision,
                supported = m is not null,
                id = info.Id,
                firmware = $"{info.UsbVersion >> 8}.{info.UsbVersion & 0xFF:D2}",
                layout = m is null ? null : new
                {
                    width = m.LayoutW, height = m.LayoutH,
                    frame = KeyboardModels.FrameImage,
                    plate = new { x = KeyboardModels.FramePlate.X, y = KeyboardModels.FramePlate.Y, w = KeyboardModels.FramePlate.W, h = KeyboardModels.FramePlate.H },
                    keys = m.Keys.Select((k, i) => new
                    {
                        code = k.Code, label = k.Label, x = k.X, y = k.Y, w = k.W, h = k.H,
                        present = slots[i] >= 0,
                        advanced = slots[i] >= 0 && !KeyboardConfig.IsNormal(cfg.Mode[slots[i]]),
                    }),
                },
                limits = new { step = lim.Step, travelMin = lim.TravelMin, travelMax = lim.TravelMax, rtMin = lim.RtMin, rtMax = lim.RtMax, deadZoneMin = lim.DeadZoneMin, deadZoneMax = lim.DeadZoneMax },
            },
            config = m is null ? null : KbConfigDto.From(cfg, m, lim),
            warnings = cfg.Warnings,
            hex = KbDump(cfg),
        };
    }

    KeyboardConfig KbEdited(KbConfigDto dto)
    {
        var kb = _kb ?? throw new InvalidOperationException("Nenhum teclado conectado.");
        var read = kb.Config ?? throw new InvalidOperationException("Leia o teclado antes de aplicar.");
        var m = kb.Model ?? throw new InvalidOperationException("Este modelo de teclado ainda não é suportado para gravação.");
        return dto.ApplyTo(read, m, kb.Limits);
    }

    object KbPreview(KbConfigDto dto)
    {
        var plan = _kb!.Plan(KbEdited(dto));
        return plan.SelectMany(o => o.Packets.Select(p => new { address = o.Label, bytes = Protocol.Keyboard.KbPacket.Hex(p.AsSpan(0, 16)) + " …" }));
    }

    object KbApply(KbConfigDto dto)
    {
        var kb = _kb ?? throw new InvalidOperationException("Nenhum teclado conectado.");
        int n = kb.Apply(KbEdited(dto), _backupDir);
        Log($"{n} pacote(s) confirmados. Backup em {_backupDir}");
        return new { count = n, state = KbState(kb, kb.Config!) };
    }

    object? KbImportBackup()
    {
        var kb = _kb ?? throw new InvalidOperationException("Nenhum teclado conectado.");
        var path = _onUi(() =>
        {
            using var dlg = new OpenFileDialog { Filter = "Backup do teclado|teclado-*.json", InitialDirectory = _backupDir };
            return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
        });
        if (path is null) return null;
        var cfg = KeyboardDevice.LoadBackup(path);
        return KbConfigDto.From(cfg, kb.Model ?? throw new InvalidOperationException("Modelo não suportado."), kb.Limits);
    }

    static string KbDump(KeyboardConfig c)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"taxa={c.ReportRateCode}  led={c.Led}");
        void Row(string name, IEnumerable<string> vals)
        {
            var a = vals.ToArray();
            for (int i = 0; i < a.Length; i += 16) sb.AppendLine($"{name,-6}{i:X2}  {string.Join(" ", a.Skip(i).Take(16))}");
        }
        Row("modo", c.Mode.Select(v => v.ToString("X2")));
        Row("acion", c.Travel.Select(v => v.ToString("X4")));
        Row("solt", c.LiftTravel.Select(v => v.ToString("X4")));
        Row("rt+", c.RtPress.Select(v => v.ToString("X4")));
        Row("rt-", c.RtLift.Select(v => v.ToString("X4")));
        Row("morta", c.DeadZone.Select(v => v.ToString("X4")));
        return sb.ToString();
    }
}
