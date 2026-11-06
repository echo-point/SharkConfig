using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SharkConfig.UI;

/// <summary>
/// Ícone da bandeja com a porcentagem da bateria do mouse escrita nele (como o "85" do iPhone), no
/// tamanho de ícone pequeno do sistema. Branco na barra de tarefas escura, preto na clara, vermelho
/// com 20% ou menos. Sem leitura, usa o ícone do app.
/// </summary>
public static class TrayBattery
{
    static bool TaskbarIsLight()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    public static Icon Render(int percent)
    {
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var color = percent <= 20 ? Color.FromArgb(255, 69, 58) : TaskbarIsLight() ? Color.FromArgb(28, 28, 30) : Color.White;
            string text = Math.Clamp(percent, 0, 100).ToString();
            // maior fonte que cabe na largura (3 dígitos ficam mais estreitos), centrada pelo desenho real
            using var path = new GraphicsPath();
            using var family = new FontFamily("Segoe UI");
            float em = size * (text.Length >= 3 ? 0.62f : 0.86f);
            path.AddString(text, family, (int)FontStyle.Bold, em, PointF.Empty, StringFormat.GenericTypographic);
            var b = path.GetBounds();
            float scale = Math.Min((size - 0.5f) / b.Width, (size * 0.86f) / b.Height);
            using var m = new Matrix();
            m.Translate(size / 2f, size / 2f);
            m.Scale(scale, scale);
            m.Translate(-(b.X + b.Width / 2f), -(b.Y + b.Height / 2f));
            path.Transform(m);
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }
        IntPtr h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
}
