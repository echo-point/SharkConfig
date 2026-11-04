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
        ApplicationConfiguration.Initialize();
        // --page overview|performance|dpi|buttons|lighting|advanced
        var page = args.SkipWhile(a => a != "--page").Skip(1).FirstOrDefault();
        Application.Run(new UI.HostForm(page));
        return 0;
    }

    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
}
