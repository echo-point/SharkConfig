using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SharkConfig.Transport;

/// <summary>
/// Transporte por feature report (teclados RY5088): o pedido vai com HidD_SetFeature e a resposta
/// volta com HidD_GetFeature, sempre no Report ID 0 com 64 bytes de dados.
/// Equivale ao sendFeatureReport/receiveFeatureReport(0) do app oficial (WebHID).
/// </summary>
public interface IFeatureTransport : IDisposable
{
    string Description { get; }
    /// <summary>Envia 64 bytes de dados (sem o Report ID).</summary>
    bool SetFeature(ReadOnlySpan<byte> data64);
    /// <summary>Lê 64 bytes de dados (sem o Report ID) ou null se falhar.</summary>
    byte[]? GetFeature();
}

public sealed class WinFeatureTransport : IFeatureTransport
{
    public const int DataLen = 64;
    readonly SafeFileHandle _h;
    public string Description { get; }

    public WinFeatureTransport(HidInfo dev)
    {
        const uint GENERIC_RW = 0xC0000000;
        _h = WinHid.CreateFileW(dev.Path, GENERIC_RW, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (_h.IsInvalid) throw new IOException($"Não abriu {dev.Path} (erro {Marshal.GetLastWin32Error()})");
        Description = $"{dev.Vid:X4}:{dev.Pid:X4} (cabo)";
    }

    public bool SetFeature(ReadOnlySpan<byte> data64)
    {
        var buf = new byte[DataLen + 1];          // [0] = Report ID 0
        data64[..Math.Min(DataLen, data64.Length)].CopyTo(buf.AsSpan(1));
        return WinHid.HidD_SetFeature(_h, buf, buf.Length);
    }

    public byte[]? GetFeature()
    {
        var buf = new byte[DataLen + 1];
        if (!WinHid.HidD_GetFeature(_h, buf, buf.Length)) return null;
        return buf[1..];
    }

    public void Dispose() => _h.Dispose();
}
