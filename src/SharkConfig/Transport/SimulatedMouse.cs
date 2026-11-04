using SharkConfig.Protocol;

namespace SharkConfig.Transport;

/// <summary>
/// Mouse virtual que implementa o lado do firmware conforme o protocolo reconstruído.
/// Serve para desenvolver/testar a UI e o protocolo sem o hardware (o X8 ainda não chegou).
/// </summary>
public sealed class SimulatedMouse : IHidTransport
{
    readonly Queue<byte[]> _responses = new();
    public FlashImage Flash { get; }
    public int BatteryPercent { get; set; } = 87;
    public int BatteryMv { get; set; } = 3950;
    public ushort Firmware { get; set; } = 0x0102;
    public List<string> Log { get; } = [];
    public string Description => "Simulador (sem hardware)";

    public SimulatedMouse(FlashImage? initial = null) => Flash = initial ?? MouseConfig.DefaultImage();

    public void Write(byte[] r)
    {
        if (r.Length != Packet.Size || r[0] != Packet.ReportId || r[16] != Packet.Checksum(r.AsSpan(0, 16)))
        {
            Log.Add("SIM: pacote inválido " + Packet.Hex(r));
            return; // firmware real provavelmente ignora
        }
        int addr = r[3] << 8 | r[4], len = r[5] & 0x7F;
        var resp = new byte[Packet.Size];
        Array.Copy(r, resp, 6);
        switch (r[1])
        {
            case 0x08: // read flash
                Array.Copy(Flash.Data, addr, resp, 6, Math.Min(len, 10));
                break;
            case 0x07: // write flash
                Array.Copy(r, 6, Flash.Data, addr, Math.Min(len, 10));
                break;
            case 0x04: // battery: [6]=% [7]=status [8..9]=mV BE
                resp[6] = (byte)BatteryPercent; resp[7] = 0; resp[8] = (byte)(BatteryMv >> 8); resp[9] = (byte)BatteryMv;
                break;
            case 0x12:
                resp[6] = (byte)(Firmware >> 8); resp[7] = (byte)Firmware;
                break;
            case 0xF1: // CID/MID (X8 Ultra: CID 124, MID 01 do Config.ini)
                resp[6] = 124; resp[7] = 0x01;
                break;
            case 0x03:
                resp[6] = 1;
                break;
            default:
                Log.Add($"SIM: opcode 0x{r[1]:X2} não suportado");
                return;
        }
        resp[16] = Packet.Checksum(resp.AsSpan(0, 16));
        _responses.Enqueue(resp);
    }

    public byte[]? Read(int timeoutMs) => _responses.Count > 0 ? _responses.Dequeue() : null;
    public void Dispose() { }
}
