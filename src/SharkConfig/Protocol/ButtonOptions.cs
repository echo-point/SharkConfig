namespace SharkConfig.Protocol;

/// <summary>Opções de função de botão (docs/PROTOCOL.md, "Botões"). Group = seção na UI.</summary>
public sealed record ButtonOption(string Id, string Group, string Label, ButtonType Type, byte P1)
{
    public static readonly ButtonOption[] All =
    [
        new("left", "Mouse", "Clique esquerdo", ButtonType.Mouse, 0x01),
        new("right", "Mouse", "Clique direito", ButtonType.Mouse, 0x02),
        new("middle", "Mouse", "Clique do meio", ButtonType.Mouse, 0x04),
        new("back", "Mouse", "Voltar", ButtonType.Mouse, 0x08),
        new("forward", "Mouse", "Avançar", ButtonType.Mouse, 0x10),
        new("dpi-cycle", "DPI", "Alternar estágios", ButtonType.Dpi, 0x01),
        new("dpi-up", "DPI", "Aumentar DPI", ButtonType.Dpi, 0x02),
        new("dpi-down", "DPI", "Diminuir DPI", ButtonType.Dpi, 0x03),
        new("dpi-lock", "DPI", "Travar DPI", ButtonType.DpiLock, 0),
        new("scroll-up", "Rolagem", "Rolar para cima", ButtonType.VScroll, 0x01),
        new("scroll-down", "Rolagem", "Rolar para baixo", ButtonType.VScroll, 0x02),
        new("scroll-left", "Rolagem", "Rolar para a esquerda", ButtonType.HScroll, 0x01),
        new("scroll-right", "Rolagem", "Rolar para a direita", ButtonType.HScroll, 0x02),
    ];

    /// <summary>Opção correspondente à função gravada no mouse, ou null se for outra (preservada sem alteração).</summary>
    public static ButtonOption? Find(ButtonAction a) => All.FirstOrDefault(o => o.Type == a.Type && o.P1 == a.P1);

    public ButtonAction ToAction() => new(Type, P1, 0);
}
