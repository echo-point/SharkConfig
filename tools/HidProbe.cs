// Sonda HID: lista coleções de um VID/PID com tamanhos de report (somente leitura de descritores)
using System; using System.Runtime.InteropServices; using Microsoft.Win32.SafeHandles; using System.Collections.Generic;
public static class HidProbe {
  [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int f; public IntPtr r; }
  [StructLayout(LayoutKind.Sequential)] public struct HIDP_CAPS { public ushort Usage, UsagePage, InLen, OutLen, FeatLen; [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] r; public ushort a,b,c,d,e,f,g,h,i,j; }
  [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
  [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
  [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
  [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
  [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevsW(ref Guid g, IntPtr e, IntPtr h, int f);
  [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA o);
  [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr s, ref SP_DEVICE_INTERFACE_DATA d, IntPtr b, int sz, out int req, IntPtr x);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern SafeFileHandle CreateFileW(string n, uint a, uint s, IntPtr sec, uint c, uint f, IntPtr t);
  public static List<string> Paths(string filter) {
    var res=new List<string>(); Guid g; HidD_GetHidGuid(out g); IntPtr set=SetupDiGetClassDevsW(ref g,IntPtr.Zero,IntPtr.Zero,0x12);
    var d=new SP_DEVICE_INTERFACE_DATA(); d.cbSize=Marshal.SizeOf(d);
    for(int i=0;SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref g,i,ref d);i++){ int req; SetupDiGetDeviceInterfaceDetailW(set,ref d,IntPtr.Zero,0,out req,IntPtr.Zero);
      IntPtr b=Marshal.AllocHGlobal(req); Marshal.WriteInt32(b,8); if(SetupDiGetDeviceInterfaceDetailW(set,ref d,b,req,out req,IntPtr.Zero)){ string p=Marshal.PtrToStringUni(b+4); if(p.ToLower().Contains(filter)) res.Add(p);} Marshal.FreeHGlobal(b);} return res; }
  public static string Caps(string path){ var h=CreateFileW(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero); if(h.IsInvalid) return "open fail "+Marshal.GetLastWin32Error();
    IntPtr p; if(!HidD_GetPreparsedData(h,out p)) {h.Close(); return "no preparsed";} HIDP_CAPS c; HidP_GetCaps(p,out c); HidD_FreePreparsedData(p); h.Close();
    return string.Format("UsagePage=0x{0:X4} Usage=0x{1:X2} In={2} Out={3} Feat={4}",c.UsagePage,c.Usage,c.InLen,c.OutLen,c.FeatLen); }
}
