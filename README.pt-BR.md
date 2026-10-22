# Claude Visual Chat

[English](README.md)

Um controle Avalonia que desenha conversas do [Claude Code](https://github.com/anthropics/claude-code) do jeito
que a extensão Claude Code do VS Code desenha. Entregue a ele as linhas que o `claude -p` escreve em stream-json, ou um
arquivo de sessão que o Claude Code guarda no disco; ele mostra a conversa e devolve o que o usuário fez (uma
mensagem, um sim ou não a um pedido de permissão, uma troca de modo ou de modelo, parar) como eventos .NET.

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
`Markdown.Avalonia.Tight` 11.0.2 e `System.Text.Json` 8.0.5.

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
conversation.MessageSubmitted += (_, e) => Send(StdinMessages.UserMessage(e.Text));
conversation.PermissionAnswered += (_, e) => Send(StdinMessages.PermissionResponse(e));
conversation.PermissionModeRequested += (_, e) => Send(StdinMessages.SetPermissionMode("req_" + ++ids, e.Mode));
conversation.ModelRequested += (_, e) => Send(StdinMessages.SetModel("req_" + ++ids, e.Model));
conversation.StopRequested += (_, _) => Send(StdinMessages.Interrupt("req_" + ++ids));
```

No Windows, um `claude` instalado pelo npm é `claude.cmd`: suba-o com `cmd /c claude ...` ou pelo caminho completo.

O `ChatConversation` não é thread-safe: chame-o na thread da UI, como acima. As linhas que ele não entende (em branco,
que não são JSON, de tipo desconhecido) são puladas. `ClaudeChatView.AvailableModes` define os modos de permissão
oferecidos (`plan`, `default`, `acceptEdits` e `auto` por padrão), e `AvailableModels`, os modelos (nenhum por padrão,
o que esconde a escolha de modelo). O `PermissionModeRequested` e o `ModelRequested` disparam quando o usuário pede a
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
| Ícones (`StreamGeometry`) | `Icon.ModeDefault`, `Icon.ModeAcceptEdits`, `Icon.ModePlan`, `Icon.ModeAuto`, `Icon.Send`, `Icon.Chevron` |

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
- A versão 0.1 não tem realce de sintaxe: o diff e os blocos de código saem numa cor só. O realce está previsto para a
  0.2.
- Alguns botões da extensão não estão aqui, porque dependem de recursos do host: anexar arquivos, os comandos `/` e o
  microfone.

## App de exemplo

O `samples/ClaudeVisualChat.Sample` toca as sessões gravadas de `tests/fixtures` (uma edição, comandos Bash, um
subagente, um erro, um pedido negado, uma interrupção e trocas de modo e de modelo) como stream ao vivo ou como sessão
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
