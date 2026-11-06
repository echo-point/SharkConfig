using Microsoft.Win32;

namespace SharkConfig.UI;

/// <summary>
/// "Iniciar com o Windows": um valor na chave Run do usuário (HKCU, sem precisar de administrador),
/// que abre o app direto na bandeja (--tray). Desligar apaga o valor.
/// </summary>
public static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "SharkConfig";

    static string Command => $"\"{Environment.ProcessPath}\" --tray";

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(Name) is string v && v.Length > 0;
        }
    }

    public static void Set(bool enabled)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled) k.SetValue(Name, Command);
        else if (k.GetValue(Name) is not null) k.DeleteValue(Name, false);
    }

    /// <summary>Se o app mudou de pasta, a entrada antiga aponta para o lugar errado: atualiza.</summary>
    public static void Refresh()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k?.GetValue(Name) is string v && v != Command) k.SetValue(Name, Command);
        }
        catch { /* sem permissão: mantém como está */ }
    }
}
