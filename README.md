<p align="center"><img src="assets/icon.svg" width="112" alt="Ícone do SharkConfig"></p>

# SharkConfig

Configurador **não oficial** e leve para o mouse **Attack Shark X8 Ultra/Ultimate** no Windows.
Interface inspirada no macOS e no Logitech G HUB, protocolo reconstruído por engenharia reversa
(interoperabilidade) do software oficial.

> Projeto independente, sem vínculo com a Attack Shark. Use por sua conta e risco.

## O que dá para ajustar

| Área | Ajustes |
|---|---|
| Desempenho | taxa de atualização 125–8000 Hz, modo do sensor (LP/HP/Corded), distância de levantamento, Motion Sync, correção de linha reta, controle de ondulação, desempenho máximo, tempo para dormir, debounce |
| Sensibilidade | 1 a 8 estágios de 50 a 42.000 DPI, cor de cada estágio, estágio ativo |
| Botões | cliques, voltar/avançar, DPI (ciclo, +, −, travar), rolagem |
| Iluminação | efeito, brilho e velocidade do indicador de DPI |
| Avançado | leitura da memória do mouse, backup e restauração |

As configurações ficam gravadas **na memória do próprio mouse**: aplique e feche o app. Ele não roda
em segundo plano nem inicia com o Windows.

## Requisitos

- Windows 10/11 com o [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (já vem no Windows 11)
- [.NET SDK 10](https://dotnet.microsoft.com/download) para compilar
- Mouse X8 Ultra pelo receptor 8K ou pelo cabo (VID `0x3554`). Feche o app oficial antes de usar.

## Compilar e rodar

```powershell
cd src\SharkConfig
dotnet build -c Release
.\bin\Release\net10.0-windows\SharkConfig.exe
```

| Opção | Efeito |
|---|---|
| `--page dpi` | abre numa página: `overview`, `performance`, `dpi`, `buttons`, `lighting`, `advanced` |
| `--list` | lista as coleções HID do PC |

### Fotos do mouse

As fotos de produto em `src/SharkConfig/wwwroot/img/mouse*.png` vêm do app oficial (© Attack Shark),
recortadas para 202×356. O app escolhe a foto pelo modelo lido do mouse ou manualmente em
Avançado → Aparência; se alguma faltar, mostra uma silhueta neutra. Para atualizá-las a partir de
uma instalação do app oficial:

```powershell
.\tools\extract-photos.ps1
```

## Segurança

- Só seis comandos podem ser enviados (cinco leituras e a gravação de configuração); atualização de
  firmware, troca de VID/PID, pareamento e reset de fábrica são bloqueados no código (`Packet.AllowedCommands`).
- Grava do mesmo jeito do app oficial: um pacote por campo alterado, só valores das mesmas listas e
  faixas, com intervalo entre pacotes.
- Antes de gravar: recusa se a leitura do mouse veio com algum campo inválido, confere cada pacote
  contra as áreas permitidas e salva um backup. Durante: exige o eco exato de cada pacote. Depois:
  relê tudo o que gravou e compara byte a byte.
- DPI só é gravado quando o modelo é identificado como PAW3950, e nenhum botão pode ficar sem o
  clique esquerdo.
- O protocolo veio de análise estática do software oficial; alguns detalhes ainda precisam de
  confirmação em hardware — veja [docs/PROTOCOL.md](docs/PROTOCOL.md).

## Estrutura

```
src/SharkConfig/
  Protocol/        pacotes, checksum, mapa da memória, modelo de configuração
  Transport/       HID do Windows (SetupAPI/hid.dll)
  MouseDevice.cs   transações, leitura da memória, aplicação com backup
  UI/              janela nativa + WebView2 e ponte JS ↔ C#
  wwwroot/         interface (HTML/CSS/JS)
  app.ico          ícone do app (16 a 256 px, gerado de assets/icon.svg)
assets/icon.svg    ícone em vetor
tools/             sonda de coleções HID e atualização das fotos
docs/PROTOCOL.md   protocolo documentado
```

O mouse é detectado sozinho ao ser conectado (o app reage ao aviso do Windows, sem ficar consultando).
A engrenagem no canto inferior esquerdo abre a aparência: tema escuro ou claro, transparência da
janela (fundo acrílico do Windows 11) e a opção de reduzir movimento.

Consumo medido (Windows 11): o processo `SharkConfig.exe` usa cerca de 15 MB de RAM privada; somando
os processos do WebView2 que desenham a interface (com a GPU ligada, para as animações e o vidro
ficarem lisos), o total fica entre ~180 MB (sem mouse) e ~225 MB (conectado). CPU parado: ~0%; ao
minimizar, a interface é suspensa. O app abre uma única janela: executá-lo de novo só traz a janela
aberta para a frente.

## Licença

Código sob a [licença MIT](LICENSE). As fotos de produto são © Attack Shark e não estão cobertas
pela licença.
