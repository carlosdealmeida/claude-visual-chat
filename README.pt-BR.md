# Claude Visual Chat

[English](README.md)

Um controle Avalonia que desenha conversas do [Claude Code](https://github.com/anthropics/claude-code) do jeito
que a extensão Claude Code do VS Code desenha. Entregue a ele as linhas que o `claude -p` escreve em stream-json, ou um
arquivo de sessão que o Claude Code guarda no disco; ele mostra a conversa e devolve o que o usuário fez (uma
mensagem, um sim ou não a um pedido de permissão, uma troca de modo, de modelo ou de effort, um comando `/`, uma imagem, parar) como eventos .NET.

> Projeto da comunidade, sem vínculo com a Anthropic e sem o endosso dela.

![O app de exemplo tocando uma sessão gravada: uma edição e um arquivo novo, cada um permitido pelo usuário](docs/screenshot-pt.png)

- **Igual à extensão.** O layout segue a extensão Claude Code do VS Code: a linha do tempo com as bolinhas, as
  chamadas de ferramenta com os subtítulos, o diff lado a lado, o pedido de permissão e a caixa de resposta, com as
  mesmas fontes, tamanhos, espaçamentos e cores.
- **As cores do VS Code atual.** O padrão são as cores do "Dark 2026", o tema escuro padrão do VS Code atual. O
  "Dark Modern" vem junto, pronto para ligar (veja [Cores](#cores)).
- **Stream ou histórico.** A saída ao vivo do `claude -p --output-format stream-json`, ou um arquivo de sessão inteiro
  (`~/.claude/projects/<pasta>/<id da sessão>.jsonl`).
- **Textos em inglês e em português do Brasil** já prontos; qualquer texto pode ser trocado.
- **Nada escondido.** A biblioteca não abre links, não baixa imagens, não roda processo e não toca em arquivo. O seu
  app sobe o `claude` e leva as linhas de um lado para o outro.

Feita para `netstandard2.0` com o Avalonia 11.0.10. As únicas dependências são `Avalonia` 11.0.10,
`Markdown.Avalonia.Tight` 11.0.2, `SkiaSharp` 2.88.7 (para reduzir e decodificar as imagens anexadas) e
`System.Text.Json` 8.0.5.

## Instalação

```sh
dotnet add package ClaudeVisualChat.Avalonia
```

O controle desenha as próprias partes, mas usa por dentro os controles padrão do Avalonia (caixa de texto, caixa de
seleção, barras de rolagem). Por isso, **o seu app precisa de um tema Avalonia**: Fluent ou Simple.

```xml
<Application xmlns="https://github.com/avaloniaui" RequestedThemeVariant="Dark">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```

Depois, ponha a view onde quiser o chat:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:cvc="using:ClaudeVisualChat.Controls">
  <cvc:ClaudeChatView Name="Chat" />
</Window>
```

## Início rápido

O seu app sobe o `claude -p` com stream-json na entrada e na saída, manda cada linha da saída para a conversa na
thread da UI e escreve na entrada o que o usuário faz:

```csharp
using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using ClaudeVisualChat;
using ClaudeVisualChat.Protocol;

var conversation = new ChatConversation();
Chat.Conversation = conversation; // a ClaudeChatView acima

var claude = Process.Start(new ProcessStartInfo("claude",
    "-p --input-format stream-json --output-format stream-json --verbose " +
    "--include-partial-messages --permission-prompt-tool stdio")
{
    WorkingDirectory = projectFolder, // a pasta em que o Claude trabalha
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    StandardOutputEncoding = Encoding.UTF8,
    UseShellExecute = false,
})!;

// claude -> view: uma linha JSON por evento, aplicada na thread da UI
claude.OutputDataReceived += (_, e) =>
{
    if (e.Data != null) Dispatcher.UIThread.Post(() => conversation.AppendLine(e.Data));
};
claude.BeginOutputReadLine();
// primeiro, o aperto de mão de controle que os SDKs oficiais mandam; os pedidos abaixo foram conferidos depois dele
Send(StdinMessages.Initialize("init_1"));

// view -> claude: cada coisa que o usuário faz é uma linha no stdin do claude
var ids = 0;
void Send(string line) { claude.StandardInput.Write(line + "\n"); claude.StandardInput.Flush(); }
conversation.MessageSubmitted += (_, e) => Send(StdinMessages.UserMessage(e.Text, e.Images));
conversation.PermissionAnswered += (_, e) => Send(StdinMessages.PermissionResponse(e));
conversation.PermissionModeRequested += (_, e) => Send(StdinMessages.SetPermissionMode("req_" + ++ids, e.Mode));
conversation.ModelRequested += (_, e) => Send(StdinMessages.SetModel("req_" + ++ids, e.Model));
conversation.EffortRequested += (_, e) => Send(StdinMessages.ApplyEffort("req_" + ++ids, e.Effort));
conversation.StopRequested += (_, _) => Send(StdinMessages.Interrupt("req_" + ++ids));
```

Um `/model x` ou `/effort x` digitado também muda o claude, mas o chip só acompanha depois do próximo `system/init` ou
de uma resposta do `GetSettings`; por isso envie o `GetSettings` depois deles.

No Windows, um `claude` instalado pelo npm é `claude.cmd`: suba-o com `cmd /c claude ...` ou pelo caminho completo.

O `ChatConversation` não é thread-safe: chame-o na thread da UI, como acima. As linhas que ele não entende (em branco,
que não são JSON, de tipo desconhecido) são puladas. `ClaudeChatView.AvailableModes` define os modos de permissão
oferecidos (`plan`, `default`, `acceptEdits` e `auto` por padrão); os modelos ficam em
`ChatConversation.AvailableModels` (nenhum por padrão, o que esconde o chip de modelo; veja
[Modelo e effort](#modelo-e-effort)). O `PermissionModeRequested` e o `ModelRequested` disparam quando o usuário pede a
troca, antes de o claude confirmar: a escolha de modo ou de modelo só mostra o novo valor quando o claude o informa.

Uma view pode mostrar várias conversas, uma de cada vez: basta pôr outra em `Conversation`. O que o usuário escreveu e
não mandou fica com a conversa dele e volta com ela.

Uma mensagem que não chega ao claude (a conexão caiu, um relé a recusou) pode ser desfeita com os argumentos do
evento, na hora, dentro do handler, ou depois:

```csharp
conversation.MessageSubmitted += (_, e) =>
{
    if (!TrySend(StdinMessages.UserMessage(e.Text))) conversation.UndoSubmit(e);
};
```

A mensagem sai da lista, o "trabalhando" some quando nada mais o mantém, e o texto volta à caixa de resposta se ela
estiver vazia (o `SubmitUndone` leva o texto, para quem quiser pô-lo em outro lugar).

## Anexos

A caixa de resposta aceita imagens: o botão **+** abre o seletor de arquivos, o Ctrl+V cola uma imagem (se a área de
transferência também tem texto, o texto vence) e dá para arrastar arquivos para a caixa. Cada uma aparece como uma
pílula com miniatura, nome, tamanho e um ×; um clique na miniatura abre a prévia em tela cheia. As imagens são
reduzidas para caber (`MaxImageSide`, 1568 px por padrão, e `ImageBudgetBytes`, 3.750.000 bytes para a mensagem
inteira), no máximo `MaxImages` (10) por mensagem; uma imagem recusada diz o motivo (`ImageTooBig`, `TooManyImages` e
`NotAnImage` no `ChatStrings`). Uma mensagem pode ter só imagens. O host as manda junto com o texto, e a mensagem do
usuário as mostra como miniaturas embaixo dela:

```csharp
conversation.MessageSubmitted += (_, e) => Send(StdinMessages.UserMessage(e.Text, e.Images));
```

O `ChatConversation.AddImageAsync(bytes)` anexa uma imagem a partir do seu código. A redução e a decodificação usam o
SkiaSharp 2.88.7, de que o pacote agora depende; o Avalonia traz as bibliotecas nativas dele. No Windows a área de
transferência é lida pelas chamadas do sistema (PNG, depois DIB e DIBV5); nos outros sistemas, pelo `image/png` do
Avalonia.

## Comandos

O botão `/` da barra, ou digitar `/` na caixa vazia, abre a paleta de comandos: um campo de filtro e, abaixo, os
comandos por nome exato, prefixo, nome que contém e descrição. As setas movem, o Enter executa, o Tab insere o comando
na caixa e o Esc fecha. Os comandos são os que o claude lista na resposta do `initialize` (o pedido de controle que o
seu app mandou no início rápido): passe essa resposta ao `AppendLine` ou defina-os você mesmo.

```csharp
conversation.Commands = new[] { new ChatCommand("review", "Revisar um pull request", "<pr>") };
```

Um comando que não é local vai ao claude como o texto de uma mensagem, como qualquer outro. Os que a própria conversa
trata (`ChatConversation.LocalCommands`: `context`, `usage`, `remote-control`, `rc` e `chrome`) disparam o
`CommandInvoked` e não mandam nada:

```csharp
conversation.CommandInvoked += (_, e) =>
{
    switch (e.Name)
    {
        case "context": Send(StdinMessages.GetContextUsage("req_" + ++ids)); break;  // a resposta enche o painel
        case "chrome": Send(StdinMessages.GetChromeStatus("req_" + ++ids)); break;
        case "remote-control": case "rc": ToggleRemoteControl(); break;               // veja abaixo
    }
};
```

No `/chrome`, o claude responde ao `GetChromeStatus` com um `ControlAcknowledged` cujo `Response` diz `connected`
ou `paired_browser`; o componente não lê isso por você: o seu app lê o `ControlAcknowledged.Response` e define o
`conversation.BrowserStatus`.

O `/context` e o `/usage` também abrem um painel sobre a caixa de resposta (`ChatConversation.ShownPanel`) com os
números que o claude informou por último: o `ContextUsage` vem da resposta do `get_context_usage`; o `UsageLimits`, das linhas
`rate_limit_event` que o claude escreve, então o seu app só precisa passar todas as linhas pelo `AppendLine`.
(`conversation.Apply(new UsageLimitsReported(limits))` é uma substituição opcional.) Sem dados ainda, o painel mostra
"…" enquanto a conversa está ocupada e "Ainda sem dados de uso" nos demais casos. O Esc fecha o painel.

## Modelo e effort

A barra mostra um chip com o modelo e o effort (**Opus 5.5 High**). Ele abre um menu com os modelos e um controle
deslizante com os níveis de effort que o modelo escolhido aceita. Os modelos vêm da conversa (não da view):

```csharp
conversation.AvailableModels = new[]
{
    new ChatModel("opus", "claude-opus-5-5", "Opus 5.5", "Para trabalho complexo e o dia a dia",
        new[] { "low", "medium", "high", "xhigh", "max" }),
    new ChatModel("haiku", "claude-haiku-4-5-20251001", "Haiku 4.5", "O mais rápido para respostas curtas",
        Array.Empty<string>()), // não aceita effort
};
```

A resposta do `initialize` traz a mesma lista, e o `AppendLine` a preenche. Como o modelo e o modo, o effort é pedido
primeiro e só aparece depois que o claude confirma: o `EffortRequested` traz o nível (`null` para o do próprio
modelo), e a resposta do `get_settings`, ou um `SettingsApplied`, move o chip. Um id de modelo com sufixo (`claude-opus-5-5[1m]`) encontra o seu modelo, e o chip mostra o sufixo
(**Opus 5.5 [1m] High**).

```csharp
conversation.EffortRequested += (_, e) =>
{
    Send(StdinMessages.ApplyEffort("req_" + ++ids, e.Effort));
    Send(StdinMessages.GetSettings("req_" + ++ids)); // a resposta diz o nível que vale agora
};
```

## Cache de prompt, browser e Remote Control

- **Cache de prompt.** Um relógio na barra conta os minutos que restam ao cache de prompt do claude, a partir do uso
  de cache da última resposta. Fica vermelho quando o cache provavelmente expirou. O `Clock` é a fonte de hora que ele
  lê (o exemplo toca a da própria gravação).
- **Browser.** O seu app informa o estado da extensão de browser: `BrowserStatus` (`Off`, `Connecting`, `Connected`,
  `NoBrowser`, `Error`, com `BrowserError`). Uma faixa sobre a caixa o conta, e o botão de desconectar dispara o
  `BrowserDisconnectRequested`; o estado só muda quando você o define.
- **Remote Control.** O `RemoteControlStatus` (`Off`, `Connecting`, `Connected`, `Error`), o `RemoteControlUrl` e o
  `RemoteControlError` mostram uma pílula na barra. Um clique nela dispara o `RemoteControlLinkRequested` (`RemoteControlLinkRequestedEventArgs`) com o link,
  que o seu app abre (a biblioteca não abre nada). As linhas `bridge_state` do claude movem o estado sozinhas.

```csharp
conversation.BrowserStatus = ChatBrowserStatus.Connected;
conversation.BrowserDisconnectRequested += (_, _) =>
{
    DisconnectBrowser(); // do seu app
    conversation.BrowserStatus = ChatBrowserStatus.Off;
};

conversation.RemoteControlStatus = ChatRemoteControlStatus.Connecting;
Send(StdinMessages.RemoteControl("req_" + ++ids, true)); // a resposta traz o link
conversation.RemoteControlLinkRequested += (_, e) =>
    Process.Start(new ProcessStartInfo(e.Url) { UseShellExecute = true });
```

## Abrir uma sessão gravada

O Claude Code guarda cada sessão em `~/.claude/projects/<pasta>/<id da sessão>.jsonl`. Para mostrar uma:

```csharp
conversation.WorkingDirectory = @"C:\work\my-app"; // caminhos dentro desta pasta aparecem relativos
conversation.LoadTranscript(File.ReadLines(sessionFile));
```

O `LoadTranscript` substitui o que a conversa tinha. As linhas que não fazem parte da conversa (anexos, histórico de
arquivos e afins) são puladas.

## Cores

Todas as cores, as duas fontes, os tamanhos de texto e alguns lugares vêm de recursos chamados `ClaudeChat.*` (as
chaves estão na tabela abaixo). O resto é fixo no tema: os títulos do Markdown (26, 19,5 e 15,2 px), algumas alturas
de linha e os espaçamentos, cantos e alturas medidos da extensão. Redefina qualquer chave nos recursos da view:

```xml
<cvc:ClaudeChatView>
  <cvc:ClaudeChatView.Resources>
    <SolidColorBrush x:Key="ClaudeChat.Background" Color="#101010" />
  </cvc:ClaudeChatView.Resources>
</cvc:ClaudeChatView>
```

As cores padrão são as do tema "Dark 2026" do VS Code. Para as do "Dark Modern", junte o dicionário dele aos recursos
da view:

```xml
<cvc:ClaudeChatView>
  <cvc:ClaudeChatView.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ResourceInclude Source="avares://ClaudeVisualChat.Avalonia/Themes/DarkModern.axaml" />
      </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
  </cvc:ClaudeChatView.Resources>
</cvc:ClaudeChatView>
```

As chaves, todas com o prefixo `ClaudeChat.`:

| Grupo | Chaves |
| --- | --- |
| Cores do tema (mudam com o tema do VS Code) | `Background`, `Surface`, `Border`, `BorderStrong`, `Foreground`, `ForegroundMuted`, `Accent`, `AccentFill`, `AccentForeground`, `FocusBorder`, `Warning`, `Error`, `Success`, `InputBackground`, `InputBorder`, `InputActiveBorder`, `PermissionActiveBorder`, `InputPlaceholder`, `HoverBackground`, `OptionBorder`, `TimelineLine`, `Dot`, `ToolBackground`, `CodeBackground`, `CodeForeground`, `CodeBlockBackground`, `PillBackground`, `PillHoverBackground` |
| Cores próprias da extensão | `DotSucceeded`, `DotFailed`, `DotRejected`, `SendBackground`, `SendForeground`, `FocusRing`, `Spinner` |
| Sombras da caixa de resposta (`BoxShadows`) | `ComposerShadow`, `ComposerFocusShadow`, `ComposerFocusShadowAcceptEdits`, `ComposerFocusShadowPlan`, `ComposerFocusShadowAuto` |
| Diff | `DiffBackground`, `DiffForeground`, `DiffRemoved`, `DiffAdded`, `DiffRemovedText`, `DiffAddedText`, `DiffHatch`, `DiffGuide`, `DiffCurrentLine`, `DiffSign`, `DiffShadow`, `DiffFade` |
| Fontes | `FontFamily` (`Segoe UI, Segoe WPC, Inter, sans-serif`), `MonoFontFamily` (`Consolas, Courier New, monospace`) |
| Tamanhos | `FontSize`, `LineHeight`, `SmallFontSize`, `SmallLineHeight`, `CodeFontSize`, `EditorFontSize`, `EditorLineHeight`, `PromptFontSize`, `PromptLineHeight`, `PromptCodeFontSize` |
| Lugares | `ListPadding`, `ComposerMaxWidth`, `ScrollBarGutter` |
| Ícones (`StreamGeometry`) | `Icon.ModeDefault`, `Icon.ModeAcceptEdits`, `Icon.ModePlan`, `Icon.ModeAuto`, `Icon.Send`, `Icon.Chevron`, `Icon.Add`, `Icon.Slash`, `Icon.Check`, `Icon.Clock`, `Icon.Close`, `Icon.Remove` |
| Menus e paleta de comandos | `MenuBackground`, `MenuForeground`, `ListHoverBackground`, `ListActiveBackground`, `ListActiveForeground`, `EffortThumbShadow` |
| Anexos | `AttachmentBackground`, `AttachmentHoverBorder`, `AttachmentRemoveFade`, `PreviewOverlay`, `PreviewShadow` |
| Faixas e Remote Control | `BannerBackground`, `BannerForeground`, `BannerTint`, `BannerErrorBackground`, `BannerErrorForeground`, `RemoteControlForeground`, `RemoteControlHoverBackground`, `RemoteControlHoverForeground` |
| Painéis `/context` e `/usage` | `ContextColor1` a `ContextColor6` (as cores de gráfico da barra de contexto), `PanelShadow` |

O `ScrollBarGutter` é o espaço que a lista deixa à direita para uma barra de rolagem desenhada por cima dela, como a do
Fluent. Uma barra com coluna própria, como a do Simple, já tem o seu espaço, e a lista fica com a largura toda.

## Textos

Os textos vêm do `ChatStrings`: `ChatStrings.English` (o padrão) ou `ChatStrings.PortugueseBrazil`. Ele é um record,
então troque só o que precisar, por cópia:

```csharp
Chat.Strings = ChatStrings.PortugueseBrazil;
Chat.Strings = ChatStrings.PortugueseBrazil with { InputPlaceholder = "Pergunte ao Claude…", TimeFormat = "H:mm" };
```

Os números usam os separadores do próprio record (`ThousandsSeparator` e `DecimalSeparator`), nunca a cultura do
sistema.

## Somente leitura

Defina `IsReadOnly` para mostrar uma conversa sem deixar o usuário agir sobre ela: uma sessão encerrada, uma máquina
fora do ar, um visualizador.

```xml
<controls:ClaudeChatView Conversation="{Binding Chat}" IsReadOnly="True" />
```

A caixa de resposta e o pedido de permissão ficam ocultos; a lista continua sendo desenhada, rolando e deixando
selecionar o texto. Os pedidos pendentes continuam em `ChatConversation.PendingPermissions` e a caixa de resposta
guarda o rascunho, então voltar `IsReadOnly` para `false` mostra tudo de novo.

## Limitações

- O formato stream-json não é uma API estável e pode mudar entre versões do `claude`. O controle foi feito com o
  `claude` 2.1.286 e ignora o que não conhece; assim, um tipo novo de linha fica de fora em vez de quebrar a view.
- Alguns botões da extensão não estão aqui, porque dependem de recursos do host: anexar arquivos que não sejam
  imagens e o microfone.

## App de exemplo

O `samples/ClaudeVisualChat.Sample` toca as sessões gravadas de `tests/fixtures` (uma edição, comandos Bash, um
subagente, um erro, um pedido negado, uma interrupção, trocas de modo e de modelo, uma imagem, comandos `/` e uma troca de effort) como stream ao vivo ou como sessão
gravada, em inglês ou em português. O painel da direita mostra cada evento que a view devolve e a linha que o seu app
escreveria no stdin por ele.

```sh
dotnet run --project samples/ClaudeVisualChat.Sample
```

Ele também tira os prints desta página sem abrir nada na tela: toca uma gravação até o fim, responde sim a cada
pedido, salva a janela em 1280×800 e sai.

```sh
dotnet run --project samples/ClaudeVisualChat.Sample -- --screenshot docs/screenshot.png --scenario edit
dotnet run --project samples/ClaudeVisualChat.Sample -- --screenshot docs/screenshot-pt.png --scenario edit --lang pt
```

Com `--history`, o print mostra o arquivo de sessão da gravação em vez do stream.

## Gravar sessões novas

O `tools/gravar.py` (Python 3, só a biblioteca padrão) grava sessões reais do `claude -p` e as limpa para o
repositório. Os cenários ficam em `tools/cenarios.json`.

```sh
python -I tools/gravar.py edit      # um cenário
python -I tools/gravar.py --todos   # todos
```

Cada cenário roda numa pasta temporária nova com o modelo `haiku`, `--setting-sources project` e
`--strict-mcp-config`, para que as suas configurações, servidores MCP, skills e plugins fiquem de fora. Custa alguns
centavos de dólar. Depois, o gravador guarda só os campos que os testes usam, troca a pasta por `C:\demo\app` e o seu
nome de usuário por `demo`, e escreve `<cenário>.stream.ndjson` e `<cenário>.transcript.jsonl` em `tests/fixtures`. O
`FixturePrivacyTests` procura nos arquivos pastas de usuário que não sejam a do `demo` e, quando você as escreve uma por
linha em `tests/private-words.txt` (que o git ignora), as suas próprias palavras: o seu nome de usuário, a empresa, os
clientes. Mesmo assim, leia os arquivos antes do commit. Os testes do próprio gravador: `python -I tools/test_gravar.py`.

## Compilar

```sh
dotnet build -c Release
dotnet test
```

Os testes de UI rodam sem tela (headless), com o Skia e as fontes do sistema. Por isso, no Linux, instale uma fonte
antes (por exemplo, `sudo apt-get install fonts-dejavu-core`). Onde faltam a Segoe UI e a Consolas, a view usa as
fontes sans-serif e monospace do sistema.

## Licença

[MIT](LICENSE). Copyright (c) 2026 carlosdealmeida.
