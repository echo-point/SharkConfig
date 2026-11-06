namespace SharkConfig.Protocol.Keyboard;

/// <summary>Uma tecla do desenho do teclado: posição em px no quadro do layout e uso HID.</summary>
public sealed record KeyCap(string Code, string Label, int X, int Y, int W, int H, int Hid);

/// <summary>
/// Modelo identificado pelo id que o teclado devolve no comando 0x8F.
/// <para><see cref="Slots"/>: para cada uma das 128 posições da matriz do firmware, o uso HID da tecla
/// (0 = vazio, −1 = Fn, 0x1000|x = mídia/consumer x). Os dados por tecla do comando 0xE5/0x65 seguem
/// essa mesma ordem.</para>
/// </summary>
public sealed record KeyboardModel(uint Id, string Name, string Revision, int[] Slots, int LayoutW, int LayoutH, KeyCap[] Keys)
{
    public const int SlotCount = 128;

    /// <summary>Posição da matriz de cada tecla do desenho (null se a tecla não existe nesta revisão).</summary>
    public int? SlotOf(KeyCap k)
    {
        int i = Array.IndexOf(Slots, k.Hid);
        return i >= 0 ? i : null;
    }
}

public static class KeyboardModels
{
    // Matrizes padrão (defaultMatrix) dos modelos ry5088_x68rt004_8k_dm, ry5088_x682_8k_dm e
    // ry5088_x68v2_8k_dm do app oficial, reduzidas ao uso HID de cada posição.
    static readonly int[] SlotsRt004 = [0,41,43,57,225,224,0,30,20,4,100,0,0,31,26,22,29,227,0,32,8,7,27,226,0,33,21,9,6,0,0,34,23,10,25,0,0,35,28,11,5,44,0,36,24,13,17,0,0,37,12,14,16,0,0,38,18,15,54,-1,0,39,19,51,55,228,0,45,47,52,56,0,0,46,48,50,229,80,0,42,49,40,82,81,0,76,75,78,0,79,4329,4330,4322,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0];
    static readonly int[] SlotsX682 = SlotsRt004;   // idêntica no app oficial
    static readonly int[] SlotsV2 = [0,41,43,57,225,224,58,30,20,4,100,227,59,31,26,22,29,226,60,32,8,7,27,0,61,33,21,9,6,0,62,34,23,10,25,0,63,35,28,11,5,44,64,36,24,13,17,0,65,37,12,14,16,0,66,38,18,15,54,0,67,39,19,51,55,-1,68,45,47,52,56,228,69,46,48,50,229,80,0,42,49,40,82,81,0,76,75,78,0,79,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0];

    public const int Fn = -1;

    // ---------------- desenho do X68 HE ----------------
    // A foto do produto (wwwroot/img/keyboard-x68.png, recorte de 498×182 px sem a alça) é o corpo do
    // teclado; as teclas do app são desenhadas por cima, nas posições medidas na foto. As fileiras
    // seguem a grade de um 65%: 16 unidades de 28,83 px a partir de x = 22,6 e 5 fileiras de 28,7 px a
    // partir de y = 14,5 (folgas iluminadas medidas na foto). Coordenadas em décimos de px do recorte.
    const double GridX = 22.6, Unit = 28.83, GridY = 14.5, RowH = 28.7, Inset = 2.0;
    public const int FrameW = 4980, FrameH = 1820;
    public const string FrameImage = "img/keyboard-x68.png";
    /// <summary>Área das teclas na foto (a placa, coberta pelo desenho do app).</summary>
    public static readonly (int X, int Y, int W, int H) FramePlate =
        // 2,5 px além da grade: a luz da foto vaza um pouco para fora das teclas da borda
        ((int)Math.Round((GridX - 2.5) * 10), (int)Math.Round((GridY - 2) * 10), (int)Math.Round((16 * Unit + 5) * 10), (int)Math.Round((5 * RowH + 4) * 10));

    static KeyCap K(string code, string label, int hid, int row, double a, double b) => new(code, label,
        (int)Math.Round((GridX + a * Unit + Inset) * 10), (int)Math.Round((GridY + row * RowH + Inset) * 10),
        (int)Math.Round(((b - a) * Unit - 2 * Inset) * 10), (int)Math.Round((RowH - 2 * Inset) * 10), hid);

    /// <summary>As 66 teclas do X68 HE (legendas como as do teclado: duas nos números e símbolos).</summary>
    static readonly KeyCap[] X68Keys =
    [
        K("Escape","Esc",41,0,0,1), K("Digit1","1 !",30,0,1,2), K("Digit2","2 @",31,0,2,3), K("Digit3","3 #",32,0,3,4),
        K("Digit4","4 $",33,0,4,5), K("Digit5","5 %",34,0,5,6), K("Digit6","6 ^",35,0,6,7), K("Digit7","7 &",36,0,7,8),
        K("Digit8","8 *",37,0,8,9), K("Digit9","9 (",38,0,9,10), K("Digit0","0 )",39,0,10,11), K("Minus","- _",45,0,11,12),
        K("Equal","= +",46,0,12,13), K("Backspace","⟵",42,0,13,15), K("Delete","Del",76,0,15,16),

        K("Tab","Tab",43,1,0,1.5), K("KeyQ","Q",20,1,1.5,2.5), K("KeyW","W",26,1,2.5,3.5), K("KeyE","E",8,1,3.5,4.5),
        K("KeyR","R",21,1,4.5,5.5), K("KeyT","T",23,1,5.5,6.5), K("KeyY","Y",28,1,6.5,7.5), K("KeyU","U",24,1,7.5,8.5),
        K("KeyI","I",12,1,8.5,9.5), K("KeyO","O",18,1,9.5,10.5), K("KeyP","P",19,1,10.5,11.5), K("BracketLeft","[ {",47,1,11.5,12.5),
        K("BracketRight","] }",48,1,12.5,13.5), K("Backslash","\\ |",49,1,13.5,15), K("PageUp","PgUp",75,1,15,16),

        K("CapsLock","Caps",57,2,0,1.75), K("KeyA","A",4,2,1.75,2.75), K("KeyS","S",22,2,2.75,3.75), K("KeyD","D",7,2,3.75,4.75),
        K("KeyF","F",9,2,4.75,5.75), K("KeyG","G",10,2,5.75,6.75), K("KeyH","H",11,2,6.75,7.75), K("KeyJ","J",13,2,7.75,8.75),
        K("KeyK","K",14,2,8.75,9.75), K("KeyL","L",15,2,9.75,10.75), K("Semicolon","; :",51,2,10.75,11.75), K("Quote","' \"",52,2,11.75,12.75),
        K("Enter","Enter",40,2,12.75,15), K("PageDown","PgDn",78,2,15,16),

        K("ShiftLeft","⇧ Shift",225,3,0,2.25), K("KeyZ","Z",29,3,2.25,3.25), K("KeyX","X",27,3,3.25,4.25), K("KeyC","C",6,3,4.25,5.25),
        K("KeyV","V",25,3,5.25,6.25), K("KeyB","B",5,3,6.25,7.25), K("KeyN","N",17,3,7.25,8.25), K("KeyM","M",16,3,8.25,9.25),
        K("Comma",", <",54,3,9.25,10.25), K("Period",". >",55,3,10.25,11.25), K("Slash","/ ?",56,3,11.25,12.25),
        K("ShiftRight","⇧ Shift",229,3,12.25,14), K("ArrowUp","↑",82,3,14,15),

        K("ControlLeft","Ctrl",224,4,0,1.25), K("MetaLeft","Win",227,4,1.25,2.5), K("AltLeft","Alt",226,4,2.5,3.75),
        K("Space","",44,4,3.75,10), K("Fn","Fn",Fn,4,10,11.25), K("ControlRight","Ctrl",228,4,11.25,12.5),
        K("ArrowLeft","←",80,4,13,14), K("ArrowDown","↓",81,4,14,15), K("ArrowRight","→",79,4,15,16),
    ];

    /// <summary>Ids do X68HE na lista de dispositivos do app oficial (todos VID 3151, PID 502D).</summary>
    public static readonly KeyboardModel[] All =
    [
        new(2270, "X68HE", "rt004", SlotsRt004, FrameW, FrameH, X68Keys),
        new(2472, "X68HE", "x682", SlotsX682, FrameW, FrameH, X68Keys),
        new(2902, "X68HE", "v2", SlotsV2, FrameW, FrameH, X68Keys),
    ];

    public static KeyboardModel? Find(uint id) => All.FirstOrDefault(m => m.Id == id);
}
