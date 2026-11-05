using System.Runtime.InteropServices;
using SharkConfig.Transport;

namespace SharkConfig;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--list"))
        {
            AttachConsole(-1);
            foreach (var d in WinHid.Enumerate()) Console.WriteLine($"{d}\n    {d.Path}");
            return 0;
        }
        // Instância única: o WebView2 de duas janelas disputaria a mesma pasta de dados (e o mesmo mouse).
        // A segunda execução só traz a janela que já existe para a frente e sai.
        using var single = new Mutex(true, @"Local\SharkConfig.InstanciaUnica", out bool first);
        if (!first)
        {
            AllowSetForegroundWindow(-1);   // ASFW_ANY: deixa a outra instância vir para a frente
            PostMessage((IntPtr)0xFFFF, UI.HostForm.ShowWindowMessage, 0, 0);   // HWND_BROADCAST
            return 0;
        }
        ApplicationConfiguration.Initialize();
        // --page overview|performance|dpi|buttons|lighting|advanced
        var page = args.SkipWhile(a => a != "--page").Skip(1).FirstOrDefault();
        Application.Run(new UI.HostForm(page));
        return 0;
    }

    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, nint w, nint l);
}
