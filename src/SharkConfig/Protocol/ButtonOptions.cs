namespace SharkConfig.Protocol;

/// <summary>Opções de função de botão (docs/PROTOCOL.md, "Botões"). Group = seção na UI.</summary>
public sealed record ButtonOption(string Id, string Group, string Label, ButtonType Type, byte P1, ushort? Consumer = null)
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
        new("media-play", "Mídia", "Reproduzir/pausar", ButtonType.Combo, 0, 0x00CD),
        new("media-next", "Mídia", "Próxima faixa", ButtonType.Combo, 0, 0x00B5),
        new("media-prev", "Mídia", "Faixa anterior", ButtonType.Combo, 0, 0x00B6),
        new("media-mute", "Mídia", "Sem som", ButtonType.Combo, 0, 0x00E2),
        new("media-volup", "Mídia", "Aumentar volume", ButtonType.Combo, 0, 0x00E9),
        new("media-voldown", "Mídia", "Diminuir volume", ButtonType.Combo, 0, 0x00EA),
        new("polling", "Sistema", "Alternar taxa de atualização", ButtonType.PollingSwitch, 0),
        new("profile", "Sistema", "Trocar configuração", ButtonType.ConfigSwitch, 0),
        new("disabled", "Sistema", "Desativado", ButtonType.Disabled, 0),
    ];

    public static ButtonOption? Find(ButtonAction a) =>
        All.FirstOrDefault(o => o.Type == a.Type && (o.Consumer is null ? o.P1 == a.P1 : o.Consumer == a.Consumer));

    public ButtonAction ToAction() => new(Type, P1, 0) { Consumer = Consumer };
}
