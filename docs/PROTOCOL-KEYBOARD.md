# Protocolo HID dos teclados HE da Attack Shark (X68 HE)

Documentação reconstruída por análise estática do software oficial "Attack Shark Driver v4"
(v3.1.12, app Electron: `resources/app/dist/js`), para fins de interoperabilidade. Nenhum código do
fabricante faz parte deste projeto.

Legenda de confiança: **[O]** observado no software oficial · **[H]** confirmado em hardware (X68HE v2,
firmware 0x0504) · **[I]** inferido · **[?]** a confirmar.

## Dispositivo

| Item | Valor |
|---|---|
| Plataforma | RY5088 (o mesmo driver serve ~50 teclados Attack Shark e de outras marcas) |
| VID | `0x3151` [O][H] |
| PID pelo cabo | `0x502D` no X68 HE [O][H]; outros modelos usam `0x5029`, `0x502F`, `0x5030` |
| Interface de configuração | `mi_02`, usage page `0xFFFF`, usage `0x02`, feature report de 65 bytes [O][H] |
| Identificação do modelo | comando `0x8F`: id em `resp[1..4]` (u32 little-endian) [O][H] |

O mesmo VID/PID é compartilhado por vários teclados; o id do `0x8F` decide o modelo. Ids do X68 HE:

| Id | Nome no app oficial | Revisão |
|---|---|---|
| 2270 | `ry5088_x68rt004_8k_dm` | rt004 |
| 2472 | `ry5088_x682_8k_dm` | x682 (matriz idêntica à rt004) |
| 2902 | `ry5088_x68v2_8k_dm` | v2 [H] |

## Transporte [O][H]

Feature reports no **Report ID 0**, 64 bytes de dados. Pedido: `HidD_SetFeature` (65 bytes, `[0]=0`);
resposta: `HidD_GetFeature` (65 bytes, dados a partir de `[1]`). No WebHID do app oficial isso é
`sendFeatureReport(0, …)` / `receiveFeatureReport(0)`.

```
[0]=cmd  [1..6]=parâmetros  [7]=checksum (modo Bit7)  [8..63]=dados
checksum Bit7 = 255 − (soma de [0..6]) & 0xFF
checksum Bit8 = 255 − (soma de [0..7]) & 0xFF, gravado em [8]   (só a iluminação 0x07 usa)
```

- Pacotes menores que 8 bytes são completados com zeros antes do checksum.
- Cadência do app oficial: 10 ms antes de cada pacote, 10 ms entre o pedido e a leitura da resposta,
  100 ms depois de cada gravação (`vendorSleep`). Até 3 tentativas por leitura.
- A maioria das respostas ecoa o comando em `resp[0]` e traz o próprio checksum Bit7 em `resp[7]` [H].
  As leituras de tabela (`0xE5`) devolvem 64 bytes de dados crus, sem cabeçalho [H].

## Comandos

| cmd | Função | SharkConfig |
|---|---|---|
| `0x8F` | id do modelo (`[1..4]`) e versão USB (`[8]<<8 \| [7]`) | leitura |
| `0x80` | versão RF (`[2]<<8 \| [1]`, 0 no cabo) | leitura |
| `0x83` / `0x03` | taxa de atualização em `[2]`: 0=8000, 1=4000, 2=2000, 3=1000, 4=500, 5=250, 6=125 Hz | leitura e gravação |
| `0x87` / `0x07` | iluminação principal (ver abaixo) | leitura e gravação |
| `0xE5` / `0x65` | tabelas por tecla do switch magnético (ver abaixo) | leitura e gravação |
| `0x84`, `0x85`, `0x86`, `0x88`, `0x89`, `0x8A`, `0x91` | perfil, LED liga/desliga, debounce, LED lateral, opções, matriz de teclas, tempo para dormir | leitura (só `--kb-probe`) |
| `0x04`, `0x06`, `0x08`, `0x09`, `0x0A`, `0x0B`, `0x10`, `0x11` | perfil, debounce, LED lateral, opções, remapeamento, macros, camada Fn, tempo para dormir | não implementado |
| `0x01` | **reset de fábrica** | **bloqueado** |
| `0x0C`, `0x12`, `0x25`, `0x28`, `0x29`, `0x32` | imagens, GIF e telas | **bloqueado** |
| `0x1B`, `0x1C`, `0x1E` | relatório e **calibração** dos switches magnéticos | **bloqueado** |
| `0x50`, `0xAC`, bootloader | **gravar SKU, apagar a flash, atualizar firmware** | **bloqueado — risco de inutilizar o teclado** |

## Iluminação `0x87` / `0x07` [O][H]

```
[1]=efeito  [2]=4 − velocidade (0–4)  [3]=brilho (0–4)  [4]=direção<<4 | cor  [5..7]=R G B
cor (nibble baixo de [4]): 0–6 cor predefinida, 7 = cor RGB de [5..7], 8 = arco-íris
```

Efeitos (`LightList`): 0 desligada, 1 fixa, 2 respiração, 3 neon, 4 onda, 5 ondulação, 6 gotas,
7 serpente, 8 reação ao toque, 9 convergência, 10 senoide, 11 caleidoscópio, 12 onda em linha,
13 imagem do usuário, 14 laser, 15 onda circular, 16 cintilante, 17 chuva, 18 meteoro, 19 apaga ao
toque, 20/22 música, 21 cor da tela. O SharkConfig não oferece 13, 20, 21 e 22, que dependem do
software rodando no PC. O app oficial troca a cor `#FFFFFF` por `#FAFFFA` ao gravar (e o inverso ao ler).

Leitura no hardware: `87 04 02 04 08 FF FF FF` = onda, velocidade 2, brilho 4, direção 0, arco-íris [H].

## Switches magnéticos `0xE5` / `0x65` [O][H]

Cada tabela tem uma entrada por **posição da matriz** (128 posições; a ordem vem da `defaultMatrix`
do modelo, ver `KeyboardModels.cs`). Leitura: `E5 op 01 página`, cada página devolve 64 bytes.

| op | Tabela | Tamanho | Padrão lido (v2) |
|---|---|---|---|
| 7 | modo: bits 0–6 = 0 normal, 2 DKS, 3 Mod-Tap, 4/5 Toggle, 7 SnapTap; bit 7 = Rapid Trigger | u8, 2 páginas | `00` |
| 0 | ponto de acionamento | u16 LE, 4 páginas | `0x0190` = 2,0 mm |
| 1 | ponto de soltura | u16 | `0x0190` = 2,0 mm |
| 2 | Rapid Trigger: sensibilidade ao pressionar | u16 | `0x003C` = 0,3 mm |
| 3 | Rapid Trigger: sensibilidade ao soltar | u16 | `0x003C` = 0,3 mm |
| 6 | zona morta no fundo | u16 | `0x003C` = 0,3 mm |
| 4, 5, 8–10 | DKS, tempo do Mod-Tap, SnapTap | | não implementado |
| 251, 252, 254 | zona morta superior, tipo de switch, calibração | | não implementado |

As duas últimas posições (126 e 127) voltam como `FF` / `FFFF` [H]; o SharkConfig as regrava como leu.

**Unidade** (`get磁轴行程步进倍数`): `mm × M`, com M = 10 se a versão (RF ou, sem ela, USB) é menor que
`0x300`, 100 entre `0x300` e `0x4FF` e 200 a partir de `0x500` [O]. O X68HE v2 com firmware `0x0504` usa
200 (passo de 0,005 mm) [H]. Faixas da interface oficial: acionamento 0,1–3,3 mm (4,0 mm em firmware
antigo), Rapid Trigger 0,01–2,0 mm, zona morta 0–1,0 mm.

**Gravação** (`setMultiMagnetismInfo`): a tabela inteira em blocos de 56 bytes:

```
[0]=0x65 [1]=op [2]=1 [3]=bloco [4]=fim [5..6]=0 [7]=checksum Bit7 [8..63]=56 bytes da tabela
```

`fim = 1` só no último bloco da última tabela da gravação. Ordem do app oficial: modo (7), depois 0, 1,
2, 3, 6. Existe também a forma por tecla `65 op 00 posição fim …`, que o SharkConfig não usa.

## Como o SharkConfig grava

- Só as tabelas e os campos que mudaram, na ordem e no formato do app oficial.
- Lista fechada de comandos (`KbPacket.AllowedCommands`) e de operações do `0x65` (`WritableMagOps`).
- Recusa gravar se a leitura veio com valores fora da faixa, se o modelo não é um X68 HE conhecido ou
  se a tecla editada usa um modo avançado (DKS, Mod-Tap, Toggle ou SnapTap).
- Salva um backup em JSON antes de gravar, relê tudo depois e compara com o que pediu.

## Confirmado em hardware (X68HE v2, firmware 0x0504)

- Gravação e releitura da taxa de atualização (`03 00 03` → 1000 Hz e de volta a 8000 Hz).
- Gravação da iluminação com checksum Bit8 (`07 04 02 02 08 FF FF FF EB` → brilho 2, e de volta).
- Gravação das tabelas de modo (op 7, 3 blocos) e acionamento (op 0, 5 blocos, `fim=1` no último):
  W em 1,5 mm com Rapid Trigger e de volta. O teclado confirma lendo a tabela já atualizada.
- Depois dos três testes, a leitura completa ficou idêntica à inicial.
- A resposta de `0x83` mantém `resp[7] = 0x7C` mesmo com `resp[2]` diferente de 0: o checksum da
  resposta não cobre os dados e não deve ser usado para validá-la.

## Pendências de validação

- Revisões rt004 e x682 (2270/2472): só a matriz foi conferida no app oficial.
- Gravação das tabelas op 1, 2, 3 e 6 (mesmo formato da op 0, ainda não exercitadas no hardware).
- Significado exato do "ponto de soltura" (op 1) com o Rapid Trigger desligado.
