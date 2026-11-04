using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SharkConfig.Transport;

public interface IHidTransport : IDisposable
{
    string Description { get; }
    void Write(byte[] report);
    /// <summary>Lê um relatório de entrada ou null no timeout.</summary>
    byte[]? Read(int timeoutMs);
}

public sealed record HidInfo(string Path, ushort Vid, ushort Pid, ushort UsagePage, ushort Usage,
    int InputLen, int OutputLen, int FeatureLen)
{
    public override string ToString() =>
        $"{Vid:X4}:{Pid:X4} up=0x{UsagePage:X4} u=0x{Usage:X2} in={InputLen} out={OutputLen} feat={FeatureLen}";
}

/// <summary>Enumeração e I/O HID via SetupAPI/hid.dll (mesmas APIs que o HIDUsb.dll oficial usa).</summary>
public static class WinHid
{
    /// <summary>VID/PIDs do Attack Shark X8 Ultra (usersetting/Config.ini, base64).</summary>
    public const ushort AttackSharkVid = 0x3554;
    public static readonly ushort[] KnownPids = [0xFB43, 0xF516, 0xF515, 0xFB44, 0xFB35, 0xF517];
    public static bool IsWirelessPid(ushort pid) => pid is 0xFB44 or 0xFB35 or 0xF517;

    public static List<HidInfo> Enumerate(ushort? vid = null)
    {
        var list = new List<HidInfo>();
        HidD_GetHidGuid(out var guid);
        IntPtr set = SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12 /*PRESENT|INTERFACE*/);
        if (set == new IntPtr(-1)) return list;
        try
        {
            var did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did); i++)
            {
                SetupDiGetDeviceInterfaceDetailW(set, ref did, IntPtr.Zero, 0, out int req, IntPtr.Zero);
                IntPtr buf = Marshal.AllocHGlobal(req);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref did, buf, req, out _, IntPtr.Zero)) continue;
                    string path = Marshal.PtrToStringUni(buf + 4)!;
                    var info = Query(path);
                    if (info is not null && (vid is null || info.Vid == vid)) list.Add(info);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return list;
    }

    static HidInfo? Query(string path)
    {
        using var h = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h.IsInvalid) return null;
        var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        if (!HidD_GetAttributes(h, ref attr)) return null;
        if (!HidD_GetPreparsedData(h, out var pp)) return null;
        try
        {
            HidP_GetCaps(pp, out var caps);
            return new HidInfo(path, attr.VendorID, attr.ProductID, caps.UsagePage, caps.Usage,
                caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength);
        }
        finally { HidD_FreePreparsedData(pp); }
    }

    /// <summary>
    /// Escolhe as coleções de escrita (Out=17) e leitura (In=17) do mouse.
    /// O oficial usa "mi_01&amp;col05" (InterfaceId=1, DeviceId=5) — usado como desempate.
    /// </summary>
    public static (HidInfo Out, HidInfo In)? FindMouse(IEnumerable<HidInfo> all)
    {
        var mine = all.Where(d => d.Vid == AttackSharkVid && KnownPids.Contains(d.Pid)).ToList();
        if (mine.Count == 0) mine = all.Where(d => d.Vid == AttackSharkVid).ToList();
        static int Score(HidInfo d) => (d.Path.Contains("mi_01", StringComparison.OrdinalIgnoreCase) ? 2 : 0)
                                     + (d.Path.Contains("col05", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        var outs = mine.Where(d => d.OutputLen == 17).OrderByDescending(Score).ToList();
        var ins = mine.Where(d => d.InputLen == 17).OrderByDescending(Score).ToList();
        if (outs.Count == 0 || ins.Count == 0) return null;
        var o = outs[0];
        var i = ins.FirstOrDefault(x => x.Path == o.Path) ?? ins.FirstOrDefault(x => x.Pid == o.Pid) ?? ins[0];
        return (o, i);
    }

    // ---------------- P/Invoke ----------------
    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }
    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevsW(ref Guid g, IntPtr e, IntPtr h, int f);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA o);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr s, ref SP_DEVICE_INTERFACE_DATA d, IntPtr b, int sz, out int req, IntPtr x);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFileW(string n, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr t);
}

/// <summary>Handles reais: escrita e leitura podem ser coleções diferentes (o DLL abre duas).</summary>
public sealed class WinHidTransport : IHidTransport
{
    readonly FileStream _out, _in;
    readonly int _inLen;
    public string Description { get; }

    public WinHidTransport(HidInfo outDev, HidInfo inDev)
    {
        const uint GENERIC_RW = 0xC0000000, OVERLAPPED = 0x40000000;
        var ho = WinHid.CreateFileW(outDev.Path, GENERIC_RW, 3, IntPtr.Zero, 3, OVERLAPPED, IntPtr.Zero);
        if (ho.IsInvalid) throw new IOException($"Não abriu {outDev.Path} (erro {Marshal.GetLastWin32Error()})");
        _out = new FileStream(ho, FileAccess.ReadWrite, 1, true);
        if (inDev.Path == outDev.Path) _in = _out;
        else
        {
            var hi = WinHid.CreateFileW(inDev.Path, GENERIC_RW, 3, IntPtr.Zero, 3, OVERLAPPED, IntPtr.Zero);
            if (hi.IsInvalid) throw new IOException($"Não abriu {inDev.Path} (erro {Marshal.GetLastWin32Error()})");
            _in = new FileStream(hi, FileAccess.ReadWrite, 1, true);
        }
        _inLen = inDev.InputLen;
        Description = $"{outDev.Vid:X4}:{outDev.Pid:X4} ({(WinHid.IsWirelessPid(outDev.Pid) ? "dongle" : "cabo")})";
    }

    public void Write(byte[] report) => _out.Write(report, 0, report.Length);

    public byte[]? Read(int timeoutMs)
    {
        var buf = new byte[_inLen];
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            int n = _in.ReadAsync(buf, 0, buf.Length, cts.Token).GetAwaiter().GetResult();
            return n > 0 ? buf : null;
        }
        catch (OperationCanceledException) { return null; }
    }

    public void Dispose() { if (!ReferenceEquals(_in, _out)) _in.Dispose(); _out.Dispose(); }
}
