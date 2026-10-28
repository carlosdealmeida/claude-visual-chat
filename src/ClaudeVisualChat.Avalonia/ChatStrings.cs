using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace ClaudeVisualChat;

/// <summary>
/// Texts of the chat view, in English (<see cref="English"/>, the default) or Brazilian Portuguese
/// (<see cref="PortugueseBrazil"/>). Change one text by copy: <c>ChatStrings.English with { Thinking = "…" }</c>.
/// Numbers use <see cref="ThousandsSeparator"/> and <see cref="DecimalSeparator"/>, never the system culture.
/// </summary>
public sealed record ChatStrings
{
    private const string Separator = " · ";

    /// <summary>No model description to translate, shared so that two default texts are equal; before <see cref="English"/>, since static fields are set in the order they are written.</summary>
    private static readonly IReadOnlyDictionary<string, string> NoModelDescriptions = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    public static ChatStrings English { get; } = new ChatStrings();

    public static ChatStrings PortugueseBrazil { get; } = new ChatStrings
    {
        Thinking = "Pensando",
        MoreLines = "+{0} linhas",
        Interrupted = "Interrompido",
        ToolInterrupted = "Ferramenta interrompida",
        Compacted = "Conversa resumida",
        ApiRetry = "Tentando de novo a chamada à API…",
        TurnFailed = "A resposta terminou com erro",
        Footer = "{0} · {1} entrada · {2} saída · US$ {3}",
        ToolRejected = "Recusado",
        InputPlaceholder = "Responder ao Claude…",
        InputPlaceholderBusy = "Pôr outra mensagem na fila…",
        InputPlaceholderTouch = "Digite / para ver os comandos",
        ThoughtFor = "Pensou por {0} s",
        RunInBackground = "Rodando em segundo plano",
        ToolReason = "Motivo:",
        AgentToolTitle = "Agent:",
        WebFetchToolTitle = "Web Fetch",
        WebSearchToolTitle = "Web Search",
        ChromeToolTitle = "Claude in Chrome",
        GlobNone = "Nenhum arquivo encontrado",
        GlobOne = "1 arquivo encontrado",
        GlobMany = "{0} arquivos encontrados",
        GrepNone = "Nenhuma correspondência encontrada",
        GrepOne = "1 linha de saída",
        GrepMany = "{0} linhas de saída",
        FetchedFrom = "Buscado em {0}",
        WebAllowed = "Permitidos:",
        WebBlocked = "Bloqueados:",
        ChromeActionCompleted = "Ação concluída",
        ChromeScreenshot = "Captura de tela",
        ChromeNavigated = "Navegação concluída",
        ChromeTabCreated = "Aba criada",
        ChromeTabsRead = "Abas lidas",
        ChromeInputCompleted = "Entrada concluída",
        ChromeWindowResized = "Janela redimensionada",
        ChromeSearchCompleted = "Busca concluída",
        ChromeGifCompleted = "Ação do GIF concluída",
        ChromeConsoleRetrieved = "Mensagens do console obtidas",
        ChromeNetworkRetrieved = "Requisições de rede obtidas",
        ChromeScriptExecuted = "Script executado",
        ChromePageRead = "Página lida",
        ChromeTextRetrieved = "Texto obtido",
        ChromeImageUploaded = "Imagem enviada",
        ChromePlanUpdated = "Plano atualizado",
        ToolShowFullText = "Pressione Enter para mostrar o texto inteiro",
        ToolCutText = "Pressione Enter para cortar o texto",
        Working = "Trabalhando…",
        Send = "Enviar",
        Stop = "Parar",
        Dictate = "Ditar",
        StopDictation = "Parar o ditado",
        TimeFormat = "HH:mm",
        EditModified = "Modificado",
        EditAdded = "Adicionou {0}",
        EditRemoved = "Removeu {0}",
        EditAddedRemoved = "Adicionou {0}, removeu {1}",
        EditFailed = "A edição falhou",
        WriteFailed = "A gravação falhou",
        WriteSucceeded = "A gravação deu certo",
        ClickToExpand = "Clique para ampliar",
        DiffViewTitle = "Comparação",
        OneLine = "1 linha",
        Lines = "{0} linhas",
        ReadLines = "(linhas {0}-{1})",
        ReadFromLine = "(da linha {0})",
        ModePlan = "Plano",
        ModeDefault = "Manual",
        ModeAcceptEdits = "Editar sozinho",
        ModeAuto = "Automático",
        PermissionRunCommand = "O Claude quer rodar este comando:",
        PermissionEditFile = "O Claude quer editar este arquivo:",
        PermissionUseTool = "O Claude quer usar {0}:",
        PermissionRunCommandQuestion = "Rodar este comando?",
        PermissionEditFileQuestion = "Editar {0}?",
        PermissionUseToolQuestion = "Usar {0}?",
        PermissionYes = "Sim",
        PermissionYesAlways = "Sim, e não perguntar de novo nesta conversa",
        PermissionYesAlwaysBeyondSession = "Sim, e não perguntar de novo",
        PermissionNo = "Não, e dizer o que fazer",
        DenyPlaceholder = "Diga ao Claude o que fazer",
        MorePending = "+{0} na fila",
        PermissionBash = "Permitir este comando bash?",
        PermissionPowerShell = "Permitir este comando do PowerShell?",
        PermissionEdit = "Fazer esta edição em {0}?",
        PermissionWrite = "Permitir gravar em {0}?",
        PermissionRead = "Permitir ler {0}?",
        PermissionGlob = "Permitir busca glob em {0}?",
        PermissionGlobAnywhere = "Permitir este comando glob",
        PermissionGrep = "Permitir grep em {0}?",
        PermissionGrepAnywhere = "Permitir este comando grep",
        PermissionWebFetch = "Permitir abrir esta url?",
        PermissionWebSearch = "Permitir pesquisar isto?",
        PermissionAllowedDomains = "Permitidos: {0}",
        PermissionBlockedDomains = "Bloqueados: {0}",
        PermissionSkill = "Usar a skill {0}?",
        PermissionSkillNote = "O Claude pode usar instruções, código ou arquivos desta skill.",
        PermissionSkillArguments = "Argumentos: {0}",
        PermissionProceed = "Quer continuar com {0}?",
        PermissionDetails = "Detalhes",
        PermissionRefuse = "Não",
        PermissionEscToCancel = "Esc para cancelar",
        PermissionCollapse = "Recolher",
        PermissionExpand = "Expandir",
        PlanAccept = "Aceitar este plano?",
        PlanCommentHint = "Selecione um trecho na prévia para comentar",
        PlanYesAutoMode = "Sim, e usar o modo automático",
        PlanYesAutoAccept = "Sim, e aceitar as edições automaticamente",
        PlanYesManual = "Sim, e aprovar as edições uma a uma",
        PlanKeepPlanning = "Não, continuar planejando",
        PlanTitle = "Plano do Claude",
        PlanModeTool = "Modo de plano",
        PlanApproved = "O usuário aprovou o plano",
        PlanStayedInPlanMode = "Continuou no modo de plano",
        PlanReadyForReview = "Pronto para revisão",
        PlanSelectToComment = "Selecione um trecho para comentar o plano",
        PlanAddComment = "Comentar",
        PlanCommentPlaceholder = "Escreva seu comentário.",
        PlanCommentCancel = "Cancelar",
        PlanComments = "Comentários ({0})",
        PlanRemoveComment = "Remover comentário",
        PlanContinuePlanning = "Continuar planejando",
        PlanOneCommentIncluded = "1 comentário irá junto como retorno",
        PlanCommentsIncluded = "{0} comentários irão junto como retorno",
        PlanSendFeedback = "Enviar retorno e continuar planejando",
        WaitingForPermission = "Aguardando permissão…",
        AllowAlwaysOneRule = "Sim, permitir {0} para {1}",
        AllowAlwaysTwoRules = "Sim, permitir {0} e {1} para {2}",
        AllowAlwaysManyRules = "Sim, permitir {0} e {1} para {2}",
        AllowAlwaysMoreRules = "mais {0}",
        AllowAlwaysOneDirectory = "Sim, permitir acesso a {0} para {1}",
        AllowAlwaysManyDirectories = "Sim, permitir acesso a {0} pastas para {1}",
        AllowAlwaysAllEdits = "Sim, permitir todas as edições nesta sessão",
        AllowAlwaysNormalMode = "Sim, voltar ao modo normal",
        AllowAlwaysThisProjectJustYou = "este projeto (só você)",
        AllowAlwaysThisProjectJustYouTip = "Grava em .claude/settings.local.json (fora do git)",
        AllowAlwaysAllProjects = "todos os projetos",
        AllowAlwaysAllProjectsTip = "Grava em ~/.claude/settings.json",
        AllowAlwaysThisProjectShared = "este projeto (compartilhado)",
        AllowAlwaysThisProjectSharedTip = "Grava em .claude/settings.json (compartilhado com o time)",
        AllowAlwaysThisSession = "esta sessão",
        AllowAlwaysThisSessionTip = "Só vale nesta sessão (não é gravado)",
        AddAttachment = "Adicionar",
        AttachPhotoOrFile = "Foto ou arquivo",
        Commands = "Comandos",
        ModeTitle = "Modo",
        Browser = "Navegador",
        StatusOff = "desligado",
        StatusConnecting = "conectando…",
        StatusOn = "ligado",
        StatusError = "erro",
        OpenRemoteControlLink = "Abrir claude.ai/code",
        RemoveAttachment = "Remover anexo",
        ShowCommands = "Mostrar o menu de comandos (/)",
        FilterCommands = "Filtrar comandos…",
        NoMatchingCommands = "Nenhum comando encontrado",
        SlashCommands = "Comandos com barra",
        CommandContextDescription = "Mostrar o uso do contexto",
        CommandUsageDescription = "Mostrar os limites de uso do plano",
        CommandRemoteControlDescription = "Ligar ou desligar o Remote Control",
        CommandChromeDescription = "Conectar ou desconectar o navegador",
        CommandCopyDescription = "Copiar a última resposta do Claude (ou /copy N para a N-ésima mais recente)",
        CopyDone = "Copiado para a área de transferência ({0} caracteres, {1})",
        CopyNothing = "Nenhuma resposta do assistente para copiar",
        CopyOnly = "Só há {0} respostas do assistente para copiar",
        CopyUsage = "Uso: /copy [N], onde N é 1 (a mais recente), 2, 3, … Recebido: {0}",
        CopyFailed = "Não foi possível copiar para a área de transferência",
        CopyWaiting = "Copiando quando a resposta terminar",
        CacheWarm = "Cache do prompt ativo, cerca de {0} min restantes.",
        CacheExpired = "Cache do prompt provavelmente expirado (parado há {0}).",
        CacheCompacted = "O cache do prompt não cobre a conversa resumida.",
        RemoteControlActive = "O Remote Control está ativo · Clique para abrir claude.ai/code · Use /remote-control para desligar",
        RemoteControlConnecting = "Conectando ao claude.ai/code…",
        RemoteControlFailed = "Erro no Remote Control: {0}",
        RemoteControlFailedHint = "Erro no Remote Control: {0} · Use /remote-control para dispensar",
        BrowserConnecting = "Conectando ao navegador…",
        BrowserConnected = "Navegador conectado",
        BrowserNoBrowsers = "Nenhum navegador conectado. Abra o Chrome com a extensão do Claude e confira se está na mesma conta do claude.ai.",
        BrowserFailed = "Erro no navegador: {0}",
        DisconnectBrowser = "Desconectar o navegador",
        SelectModel = "Selecione um modelo",
        SelectModelTitle = "Selecionar modelo",
        OtherModels = "Outros modelos",
        ModelDescriptions = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
        {
            ["For complex work and everyday tasks"] = "Para trabalhos complexos e tarefas do dia a dia",
            ["For your toughest challenges"] = "Para seus desafios mais difíceis",
            ["Most efficient for simpler tasks"] = "Mais eficiente para tarefas mais simples",
            ["Fastest for quick answers"] = "Mais rápido para respostas rápidas",
        }),
        Model = "Modelo",
        SwitchModel = "Trocar o modelo",
        Effort = "Esforço",
        EffortLow = "Baixo",
        EffortMedium = "Médio",
        EffortHigh = "Alto",
        EffortExtraHigh = "Muito alto",
        EffortMax = "Máximo",
        EffortAuto = "Automático",
        ImageTooBig = "Imagem grande demais para anexar",
        TooManyImages = "Imagens demais em uma mensagem",
        NotAnImage = "Não é uma imagem",
        ImageFiles = "Imagens",
        FileTooBig = "Arquivo maior que {0}",
        FileNotSupported = "Só arquivos PDF, de texto e CSV",
        TooManyFiles = "Até {0} arquivos por mensagem",
        FileUploading = "Enviando {0}%",
        AttachableFiles = "Imagens, PDF, texto e CSV",
        ContextTitle = "Uso do contexto",
        UsageTitle = "Uso",
        UsageUnavailable = "Ainda sem dados de uso",
        ContextUnavailable = "Ainda sem dados de contexto",
        UsageResets = "Renova {0}",
        ContextTotal = "{0} / {1} ({2} %)",
        ContextSection = "Contexto",
        ContextWindow = "Janela de contexto",
        PercentUsed = "{0}% utilizado",
        TokensOfWindow = "{0} de {1} tokens",
        PlanUsage = "Uso do plano",
        Refresh = "Atualizar",
        UsageFiveHour = "Sessão atual (5h)",
        UsageSevenDay = "Semanal (7 dias)",
        UsagePercent = "{0} %",
        DayNames = "dom seg ter qua qui sex sáb",
        AgentsChip = "{0} agentes",
        AgentsChipOne = "1 agente",
        AgentsChipTouch = "Agentes · {0}",
        AgentsChipTouchRunning = "Agentes · {0} rodando",
        ContextRingTip = "{0}% do contexto restante até o auto-compactar.",
        ContextRingHint = "Clique para ver o uso do contexto.",
        AgentMapTitle = "Mapa de agentes",
        AgentMapSubtitle = "{0} agentes · clique num agente para ver detalhes",
        AgentMapSubtitleOne = "1 agente · clique num agente para ver detalhes",
        AgentMapSubtitleTouch = "{0} agentes · toque num agente para ver os detalhes",
        AgentMapSubtitleTouchOne = "1 agente · toque num agente para ver os detalhes",
        AgentMain = "Agente principal",
        AgentSpawnedByMain = "Chamado pelo agente principal",
        AgentSpawnedBy = "Chamado por {0}",
        AgentRunning = "Rodando",
        AgentFinished = "Terminado",
        AgentFailed = "Falhou",
        AgentStopped = "Parado",
        AgentBackground = "segundo plano",
        AgentNoResult = "Sem resultado ainda",
        AgentWorkingInBackground = "Trabalhando em segundo plano",
        AgentWorkingInBackgroundOn = "Trabalhando em segundo plano: {0}",
        AgentWaitingForPermission = "Aguardando sua permissão: veja o pedido abaixo",
        AgentWaitingAlongWithNested = "Aguardando sua permissão, assim como um agente que ele iniciou: veja o pedido abaixo",
        AgentFailedWith = "Falhou: {0}",
        AgentNestedWaiting = "um agente que ele iniciou aguarda sua permissão: veja o pedido abaixo",
        AgentNestedWorking = "um agente que ele iniciou ainda está trabalhando",
        AgentNestedFailed = "um agente que ele iniciou falhou",
        AgentNestedStopped = "um agente que ele iniciou foi parado",
        AgentTasksHint = "digite /tasks para ver a transcrição dele",
        AgentTokens = "{0} tokens",
        AgentContextTokens = "{0} tokens no contexto",
        Back = "Voltar",
        Close = "Fechar",
        Dismiss = "Dispensar",
        CopyAnswer = "Copiar",
        Copied = "Copiado",
        CopyCode = "Copiar código",
        CopyCodeToClipboard = "Copiar código para a área de transferência",
        CopyResponse = "Copiar resposta",
        CopyResponseToClipboard = "Copiar resposta para a área de transferência",
        BookmarkResponse = "Marcar resposta",
        RemoveBookmark = "Desmarcar resposta",
        GoodResponse = "Boa resposta",
        BadResponse = "Resposta ruim",
        FeedbackLearnHow = "Saiba como usamos sua avaliação",
        FeedbackThanks = "Obrigado pela avaliação",
        FeedbackLearnHowShort = "Saiba como a usamos",
        FeedbackTitle = "O que deu errado?",
        FeedbackPlaceholder = "Conte mais (opcional)",
        FeedbackSkip = "Pular",
        FeedbackSend = "Enviar avaliação",
        ShowMore = "Mostrar mais",
        ShowLess = "Mostrar menos",
        ThousandsSeparator = ".",
        DecimalSeparator = ",",
    };

    /// <summary>The collapsed line of Claude's thinking, while it arrives or when its length is unknown.</summary>
    public string Thinking { get; init; } = "Thinking";

    /// <summary>The collapsed line of a whole thought; <c>{0}</c> is how many seconds it took.</summary>
    public string ThoughtFor { get; init; } = "Thought for {0}s";

    /// <summary>Under a command that runs in the background.</summary>
    public string RunInBackground { get; init; } = "Run in background";

    /// <summary>The line at the end of the list while Claude works and nothing is arriving.</summary>
    public string Working { get; init; } = "Working…";

    /// <summary>Lines left out of a tool output or diff; <c>{0}</c> is the count.</summary>
    public string MoreLines { get; init; } = "+{0} lines";

    /// <summary>The line for a request the user stopped.</summary>
    public string Interrupted { get; init; } = "Interrupted";

    /// <summary>The line for a request the user stopped while the claude used a tool.</summary>
    public string ToolInterrupted { get; init; } = "Tool interrupted";

    public string Compacted { get; init; } = "Conversation compacted";

    public string ApiRetry { get; init; } = "Retrying the API request…";

    public string TurnFailed { get; init; } = "The turn ended with an error";

    /// <summary>
    /// The line under a finished turn: <c>{0}</c> duration, <c>{1}</c> input tokens, <c>{2}</c> output tokens,
    /// <c>{3}</c> cost in dollars. Parts are separated by " · "; a part whose value is unknown is left out.
    /// </summary>
    public string Footer { get; init; } = "{0} · {1} in · {2} out · ${3}";

    /// <summary>Not used since 0.6.0: a tool call the user said no to shows a red dot, not a word.</summary>
    public string ToolRejected { get; init; } = "Rejected";

    /// <summary>The empty reply box.</summary>
    public string InputPlaceholder { get; init; } = "Reply to Claude…";

    /// <summary>The empty reply box while Claude works: what is sent now waits in line.</summary>
    public string InputPlaceholderBusy { get; init; } = "Queue another message…";

    /// <summary>The empty reply box of the touch layout: a "/" typed in it opens the command palette.</summary>
    public string InputPlaceholderTouch { get; init; } = "Type / for commands";

    /// <summary>The send button's name (it shows an arrow).</summary>
    public string Send { get; init; } = "Send";

    /// <summary>The name of the button that interrupts Claude while it works (it shows a square).</summary>
    public string Stop { get; init; } = "Stop";

    /// <summary>The name of the touch layout's microphone, which starts the dictation (see <see cref="IChatDictation"/>).</summary>
    public string Dictate { get; init; } = "Dictate";

    /// <summary>The microphone's name while it listens: a tap ends the dictation.</summary>
    public string StopDictation { get; init; } = "Stop dictation";

    /// <summary>
    /// The time over a message, in local time, as a .NET custom format read with the invariant culture
    /// (<c>h:mm tt</c>: "10:27 PM").
    /// </summary>
    public string TimeFormat { get; init; } = "h:mm tt";

    /// <summary>Under an edit that kept the line count.</summary>
    public string EditModified { get; init; } = "Modified";

    /// <summary>Under an edit that added lines; <c>{0}</c> is <see cref="Lines"/> or <see cref="OneLine"/>.</summary>
    public string EditAdded { get; init; } = "Added {0}";

    /// <summary>Under an edit that removed lines; <c>{0}</c> is <see cref="Lines"/> or <see cref="OneLine"/>.</summary>
    public string EditRemoved { get; init; } = "Removed {0}";

    /// <summary>Under an edit that added (<c>{0}</c>) and removed (<c>{1}</c>) lines.</summary>
    public string EditAddedRemoved { get; init; } = "Added {0}, removed {1}";

    /// <summary>Under an edit that failed.</summary>
    public string EditFailed { get; init; } = "Edit failed";

    /// <summary>Under a write that failed.</summary>
    public string WriteFailed { get; init; } = "Write failed";

    /// <summary>Under a write whose content is not a text (it is shown as JSON).</summary>
    public string WriteSucceeded { get; init; } = "Write succeeded";

    /// <summary>The pill over a diff in the list, under the pointer, and what a screen reader tells on it: a click opens the Diff View.</summary>
    public string ClickToExpand { get; init; } = "Click to expand";

    /// <summary>The title of the Diff View when the call names no file.</summary>
    public string DiffViewTitle { get; init; } = "Diff View";

    /// <summary>A count of one line.</summary>
    public string OneLine { get; init; } = "1 line";

    /// <summary>A count of lines (also under a write); <c>{0}</c> is the count.</summary>
    public string Lines { get; init; } = "{0} lines";

    /// <summary>Next to a Read of some lines; <c>{0}</c> the first, <c>{1}</c> the last.</summary>
    public string ReadLines { get; init; } = "(lines {0}-{1})";

    /// <summary>Next to a Read from a line to the end; <c>{0}</c> the first.</summary>
    public string ReadFromLine { get; init; } = "(from line {0})";

    /// <summary>The label of a command in the box under a tool call.</summary>
    public string ToolInput { get; init; } = "IN";

    /// <summary>The label of the output in the box under a tool call.</summary>
    public string ToolOutput { get; init; } = "OUT";

    /// <summary>The bold start of the line under a refused command, edit or write: the reason the user typed follows it.</summary>
    public string ToolReason { get; init; } = "Reason:";

    /// <summary>The name of an Agent (or Task) call, with its colon: the tool's name, the same in every language.</summary>
    public string AgentToolTitle { get; init; } = "Agent:";

    /// <summary>The name of the Web Fetch tool, the same in every language.</summary>
    public string WebFetchToolTitle { get; init; } = "Web Fetch";

    /// <summary>The name of the Web Search tool, the same in every language.</summary>
    public string WebSearchToolTitle { get; init; } = "Web Search";

    /// <summary>The name of the Claude in Chrome tools, a product's name, the same in every language.</summary>
    public string ChromeToolTitle { get; init; } = "Claude in Chrome";

    /// <summary>The line under a Glob that found nothing.</summary>
    public string GlobNone { get; init; } = "No files found";

    /// <summary>The line under a Glob that found one file.</summary>
    public string GlobOne { get; init; } = "Found 1 file";

    /// <summary>The line under a Glob that found files; <c>{0}</c> is how many.</summary>
    public string GlobMany { get; init; } = "Found {0} files";

    /// <summary>The line under a Grep with no output.</summary>
    public string GrepNone { get; init; } = "No matches found";

    /// <summary>The line under a Grep whose output is one line.</summary>
    public string GrepOne { get; init; } = "1 line of output";

    /// <summary>The line under a Grep whose output has several lines; <c>{0}</c> is how many.</summary>
    public string GrepMany { get; init; } = "{0} lines of output";

    /// <summary>The box of a Web Fetch that worked; <c>{0}</c> is the url.</summary>
    public string FetchedFrom { get; init; } = "Fetched from {0}";

    /// <summary>Starts the domains a Web Search was allowed to use.</summary>
    public string WebAllowed { get; init; } = "Allowed:";

    /// <summary>Starts the domains a Web Search was kept away from.</summary>
    public string WebBlocked { get; init; } = "Blocked:";

    /// <summary>A Claude in Chrome computer action that worked.</summary>
    public string ChromeActionCompleted { get; init; } = "Action completed";

    /// <summary>The thumbnail of a Claude in Chrome screenshot; also its name for screen readers and in the preview.</summary>
    public string ChromeScreenshot { get; init; } = "Screenshot";

    /// <summary>Claude in Chrome: a navigate that worked.</summary>
    public string ChromeNavigated { get; init; } = "Navigation completed";

    /// <summary>Claude in Chrome: a tab opened.</summary>
    public string ChromeTabCreated { get; init; } = "Tab created";

    /// <summary>Claude in Chrome: the open tabs read.</summary>
    public string ChromeTabsRead { get; init; } = "Tabs read";

    /// <summary>Claude in Chrome: a form field filled.</summary>
    public string ChromeInputCompleted { get; init; } = "Input completed";

    /// <summary>Claude in Chrome: the window resized.</summary>
    public string ChromeWindowResized { get; init; } = "Window resized";

    /// <summary>Claude in Chrome: a find that ended with no text to show.</summary>
    public string ChromeSearchCompleted { get; init; } = "Search completed";

    /// <summary>Claude in Chrome: a GIF action that ended with no text to show.</summary>
    public string ChromeGifCompleted { get; init; } = "GIF action completed";

    /// <summary>Claude in Chrome: the console read with no text to show.</summary>
    public string ChromeConsoleRetrieved { get; init; } = "Console messages retrieved";

    /// <summary>Claude in Chrome: the network requests read with no text to show.</summary>
    public string ChromeNetworkRetrieved { get; init; } = "Network requests retrieved";

    /// <summary>Claude in Chrome: a script ran.</summary>
    public string ChromeScriptExecuted { get; init; } = "Script executed";

    /// <summary>Claude in Chrome: the page read with no text to show.</summary>
    public string ChromePageRead { get; init; } = "Page read";

    /// <summary>Claude in Chrome: the page's text read with no text to show.</summary>
    public string ChromeTextRetrieved { get; init; } = "Text retrieved";

    /// <summary>Claude in Chrome: an image uploaded.</summary>
    public string ChromeImageUploaded { get; init; } = "Image uploaded";

    /// <summary>Claude in Chrome: the plan updated with no text to show.</summary>
    public string ChromePlanUpdated { get; init; } = "Plan updated";

    /// <summary>What a screen reader tells on a cut text of a tool, which a key opens whole.</summary>
    public string ToolShowFullText { get; init; } = "Press Enter to show the full text";

    /// <summary>What a screen reader tells on a text of a tool that is open, which a key cuts again.</summary>
    public string ToolCutText { get; init; } = "Press Enter to cut the text";

    /// <summary>The <c>plan</c> permission mode.</summary>
    public string ModePlan { get; init; } = "Plan";

    /// <summary>The <c>default</c> permission mode (the extension calls it "Manual").</summary>
    public string ModeDefault { get; init; } = "Manual";

    /// <summary>The <c>acceptEdits</c> permission mode.</summary>
    public string ModeAcceptEdits { get; init; } = "Edit automatically";

    /// <summary>The <c>auto</c> permission mode.</summary>
    public string ModeAuto { get; init; } = "Auto";

    /// <summary>Title of a permission request for Bash or PowerShell. Not used since 0.6.0 (see <see cref="PermissionBash"/>).</summary>
    public string PermissionRunCommand { get; init; } = "Claude wants to run this command:";

    /// <summary>Title of a permission request for Edit, Write, MultiEdit or NotebookEdit. Not used since 0.6.0 (see <see cref="PermissionEdit"/>).</summary>
    public string PermissionEditFile { get; init; } = "Claude wants to edit this file:";

    /// <summary>
    /// Title of a permission request for any other tool; <c>{0}</c> is the tool's display name. Not used since 0.6.0 (see
    /// <see cref="PermissionProceed"/>).
    /// </summary>
    public string PermissionUseTool { get; init; } = "Claude wants to use {0}:";

    /// <summary>Title of a permission request for Bash or PowerShell in the touch layout, which asks in a few words; the command is under it.</summary>
    public string PermissionRunCommandQuestion { get; init; } = "Run this command?";

    /// <summary>The touch layout's title for Edit, Write, MultiEdit or NotebookEdit; <c>{0}</c> is the name of the file (the last part of its path).</summary>
    public string PermissionEditFileQuestion { get; init; } = "Edit {0}?";

    /// <summary>The touch layout's title for any other tool; <c>{0}</c> is the tool's display name.</summary>
    public string PermissionUseToolQuestion { get; init; } = "Use {0}?";

    public string PermissionYes { get; init; } = "Yes";

    /// <summary>
    /// The touch layout's option 2 when every rule it saves lasts only for this conversation (destination <c>session</c>). The desktop
    /// card names the rule instead (<see cref="AllowAlwaysOneRule"/> and the like) since 0.6.0.
    /// </summary>
    public string PermissionYesAlways { get; init; } = "Yes, and don't ask again this session";

    /// <summary>
    /// The touch layout's option 2 when a rule it saves outlives the conversation (kept in the settings, such as
    /// <c>localSettings</c>): it says nothing about how long. Also the desktop card's option 2 when the suggestions give nothing to
    /// name.
    /// </summary>
    public string PermissionYesAlwaysBeyondSession { get; init; } = "Yes, and don't ask again";

    /// <summary>The touch layout's "No", which opens the field under it (the desktop's card says <see cref="PermissionRefuse"/>).</summary>
    public string PermissionNo { get; init; } = "No, and tell Claude what to do differently";

    /// <summary>The empty field of "No": the one <see cref="PermissionNo"/> opens, and the desktop card's, always under the options.</summary>
    public string DenyPlaceholder { get; init; } = "Tell Claude what to do instead";

    /// <summary>Requests waiting behind the one shown; <c>{0}</c> is the count.</summary>
    public string MorePending { get; init; } = "+{0} waiting";

    /// <summary>The desktop card's title for Bash; the command is in a box under it.</summary>
    public string PermissionBash { get; init; } = "Allow this bash command?";

    /// <summary>The desktop card's title for PowerShell; the command is in a box under it.</summary>
    public string PermissionPowerShell { get; init; } = "Allow this PowerShell command?";

    /// <summary>The desktop card's title for Edit; <c>{0}</c> is the file's name (the last part of its path), in the editor's font.</summary>
    public string PermissionEdit { get; init; } = "Make this edit to {0}?";

    /// <summary>The desktop card's title for Write; <c>{0}</c> is the file's name, in the editor's font.</summary>
    public string PermissionWrite { get; init; } = "Allow write to {0}?";

    /// <summary>The desktop card's title for Read; <c>{0}</c> is the file's name, in the editor's font.</summary>
    public string PermissionRead { get; init; } = "Allow reading from {0}?";

    /// <summary>The desktop card's title for Glob with a path; <c>{0}</c> is the path, in the editor's font.</summary>
    public string PermissionGlob { get; init; } = "Allow glob search in {0}?";

    /// <summary>The desktop card's title for Glob without a path.</summary>
    public string PermissionGlobAnywhere { get; init; } = "Allow this glob command";

    /// <summary>The desktop card's title for Grep with a path; <c>{0}</c> is the path, in the editor's font.</summary>
    public string PermissionGrep { get; init; } = "Allow grep in {0}?";

    /// <summary>The desktop card's title for Grep without a path.</summary>
    public string PermissionGrepAnywhere { get; init; } = "Allow this grep command";

    /// <summary>The desktop card's title for WebFetch; the url is in a box under it.</summary>
    public string PermissionWebFetch { get; init; } = "Allow fetching this url?";

    /// <summary>The desktop card's title for WebSearch; the query is in a box under it.</summary>
    public string PermissionWebSearch { get; init; } = "Allow searching for this query?";

    /// <summary>The domains a web search is kept to, in a box under its query; <c>{0}</c> is the list.</summary>
    public string PermissionAllowedDomains { get; init; } = "Allowed: {0}";

    /// <summary>The domains a web search leaves out, after <see cref="PermissionAllowedDomains"/>; <c>{0}</c> is the list.</summary>
    public string PermissionBlockedDomains { get; init; } = "Blocked: {0}";

    /// <summary>The desktop card's title for a skill; <c>{0}</c> is the skill with its "/", in the editor's font.</summary>
    public string PermissionSkill { get; init; } = "Use skill {0}?";

    /// <summary>The line under a skill's title.</summary>
    public string PermissionSkillNote { get; init; } = "Claude may use instructions, code, or files from this skill.";

    /// <summary>What a skill is given; <c>{0}</c> is the text.</summary>
    public string PermissionSkillArguments { get; init; } = "Arguments: {0}";

    /// <summary>The desktop card's title for any other tool; <c>{0}</c> is the tool's raw name, in bold.</summary>
    public string PermissionProceed { get; init; } = "Do you want to proceed with {0}?";

    /// <summary>The fold under <see cref="PermissionProceed"/> that shows the tool's input.</summary>
    public string PermissionDetails { get; init; } = "Details";

    /// <summary>The desktop card's "No", which refuses at once with what the field under the options says.</summary>
    public string PermissionRefuse { get; init; } = "No";

    /// <summary>The hint under the desktop card's field.</summary>
    public string PermissionEscToCancel { get; init; } = "Esc to cancel";

    /// <summary>The name of the desktop card's button that folds it to its title, to read the conversation behind it.</summary>
    public string PermissionCollapse { get; init; } = "Collapse";

    /// <summary>The name of the same button while the card is folded.</summary>
    public string PermissionExpand { get; init; } = "Expand";

    /// <summary>The plan card's title (an <c>ExitPlanMode</c> request); the card holds no plan text.</summary>
    public string PlanAccept { get; init; } = "Accept this plan?";

    /// <summary>The line under the desktop plan card's title.</summary>
    public string PlanCommentHint { get; init; } = "Select text in the preview to add comments";

    /// <summary>The plan card's option 1 when the view offers the auto mode: approves, and asks for that mode first.</summary>
    public string PlanYesAutoMode { get; init; } = "Yes, and use auto mode";

    /// <summary>The plan card's option 1 when the view does not offer the auto mode: approves, and asks for acceptEdits first.</summary>
    public string PlanYesAutoAccept { get; init; } = "Yes, and auto-accept";

    /// <summary>The plan card's option 2: approves, and asks for the default mode first.</summary>
    public string PlanYesManual { get; init; } = "Yes, and manually approve edits";

    /// <summary>The plan card's "No": the claude stays in plan mode, with what the field says, if anything.</summary>
    public string PlanKeepPlanning { get; init; } = "No, keep planning";

    /// <summary>
    /// The name of an <c>ExitPlanMode</c> row that carries the plan (its file's name follows it, when it has one), and the plan's
    /// document's title when the plan has no heading of its own.
    /// </summary>
    public string PlanTitle { get; init; } = "Claude’s Plan";

    /// <summary>The name of an <c>ExitPlanMode</c> row whose call came without the plan.</summary>
    public string PlanModeTool { get; init; } = "Plan Mode";

    /// <summary>The line under an <c>ExitPlanMode</c> row once the user approved the plan.</summary>
    public string PlanApproved { get; init; } = "User approved the plan";

    /// <summary>The line under an <c>ExitPlanMode</c> row once the user refused the plan (the claude stays in plan mode).</summary>
    public string PlanStayedInPlanMode { get; init; } = "Stayed in plan mode";

    /// <summary>The bold line of the banner over the plan's document while the plan waits for an answer.</summary>
    public string PlanReadyForReview { get; init; } = "Ready for review";

    /// <summary>The line under it.</summary>
    public string PlanSelectToComment { get; init; } = "Select text to add comments on the plan";

    /// <summary>The button under a passage selected in the plan's document, and the comment box's button that adds the comment.</summary>
    public string PlanAddComment { get; init; } = "Add Comment";

    /// <summary>The placeholder of the comment box's field.</summary>
    public string PlanCommentPlaceholder { get; init; } = "Add your feedback.";

    /// <summary>The comment box's button that closes it without a comment.</summary>
    public string PlanCommentCancel { get; init; } = "Cancel";

    /// <summary>The title of the comments' list in the plan card; <c>{0}</c> is how many there are.</summary>
    public string PlanComments { get; init; } = "Comments ({0})";

    /// <summary>The name and tooltip of the × that takes a comment away.</summary>
    public string PlanRemoveComment { get; init; } = "Remove comment";

    /// <summary>The plan card's title while it has comments.</summary>
    public string PlanContinuePlanning { get; init; } = "Continue planning";

    /// <summary>The line under that title with one comment.</summary>
    public string PlanOneCommentIncluded { get; init; } = "1 comment will be included as feedback";

    /// <summary>The same with more; <c>{0}</c> is how many.</summary>
    public string PlanCommentsIncluded { get; init; } = "{0} comments will be included as feedback";

    /// <summary>The plan card's only answer while it has comments: refuses the plan with them as feedback.</summary>
    public string PlanSendFeedback { get; init; } = "Send feedback and keep planning";

    /// <summary>Above the reply box, while a request waits for the typing in it to pause.</summary>
    public string WaitingForPermission { get; init; } = "Waiting for permission…";

    /// <summary>Option 2 with one rule; <c>{0}</c> is the rule ("npm"), <c>{1}</c> where it is saved (<see cref="AllowAlwaysThisSession"/> and the like).</summary>
    public string AllowAlwaysOneRule { get; init; } = "Yes, allow {0} for {1}";

    /// <summary>Option 2 with two rules; <c>{0}</c> and <c>{1}</c> are the rules, <c>{2}</c> where they are saved.</summary>
    public string AllowAlwaysTwoRules { get; init; } = "Yes, allow {0} and {1} for {2}";

    /// <summary>
    /// Option 2 with more than two rules; <c>{0}</c> is the first rule, <c>{1}</c> the others (<see cref="AllowAlwaysMoreRules"/>,
    /// with a tooltip that lists them), <c>{2}</c> where they are saved.
    /// </summary>
    public string AllowAlwaysManyRules { get; init; } = "Yes, allow {0} and {1} for {2}";

    /// <summary>The rules that <see cref="AllowAlwaysManyRules"/> does not name; <c>{0}</c> is how many.</summary>
    public string AllowAlwaysMoreRules { get; init; } = "{0} more";

    /// <summary>Option 2 with one folder; <c>{0}</c> is its name ("data/"), <c>{1}</c> where it is saved.</summary>
    public string AllowAlwaysOneDirectory { get; init; } = "Yes, allow access to {0} for {1}";

    /// <summary>Option 2 with several folders; <c>{0}</c> is how many, <c>{1}</c> where they are saved.</summary>
    public string AllowAlwaysManyDirectories { get; init; } = "Yes, allow access to {0} directories for {1}";

    /// <summary>Option 2 when the claude only offers to accept edits.</summary>
    public string AllowAlwaysAllEdits { get; init; } = "Yes, allow all edits this session";

    /// <summary>Option 2 when the claude only offers to go back to the normal mode.</summary>
    public string AllowAlwaysNormalMode { get; init; } = "Yes, return to normal mode";

    /// <summary>Where option 2 saves the rule: the local settings (<c>localSettings</c>).</summary>
    public string AllowAlwaysThisProjectJustYou { get; init; } = "this project (just you)";

    /// <summary>The tooltip of <see cref="AllowAlwaysThisProjectJustYou"/>.</summary>
    public string AllowAlwaysThisProjectJustYouTip { get; init; } = "Saves to .claude/settings.local.json (gitignored)";

    /// <summary>Where option 2 saves the rule: the user's settings (<c>userSettings</c>).</summary>
    public string AllowAlwaysAllProjects { get; init; } = "all projects";

    /// <summary>The tooltip of <see cref="AllowAlwaysAllProjects"/>.</summary>
    public string AllowAlwaysAllProjectsTip { get; init; } = "Saves to ~/.claude/settings.json";

    /// <summary>Where option 2 saves the rule: the project's settings (<c>projectSettings</c>).</summary>
    public string AllowAlwaysThisProjectShared { get; init; } = "this project (shared)";

    /// <summary>The tooltip of <see cref="AllowAlwaysThisProjectShared"/>.</summary>
    public string AllowAlwaysThisProjectSharedTip { get; init; } = "Saves to .claude/settings.json (shared with team)";

    /// <summary>Where option 2 saves the rule: nowhere, it lasts the session (<c>session</c>).</summary>
    public string AllowAlwaysThisSession { get; init; } = "this session";

    /// <summary>The tooltip of <see cref="AllowAlwaysThisSession"/>.</summary>
    public string AllowAlwaysThisSessionTip { get; init; } = "Only for this session (not saved)";

    /// <summary>The button that attaches a file or image to the message; also the title of the touch layout's "+" sheet.</summary>
    public string AddAttachment { get; init; } = "Add";

    /// <summary>The row of the touch layout's "+" sheet that opens the file dialog to attach a photo or a file.</summary>
    public string AttachPhotoOrFile { get; init; } = "Photo or file";

    /// <summary>The row of the "+" sheet that opens the command palette.</summary>
    public string Commands { get; init; } = "Commands";

    /// <summary>The row of the "+" sheet that opens the permission modes, and the title of that page.</summary>
    public string ModeTitle { get; init; } = "Mode";

    /// <summary>The row of the "+" sheet that connects or disconnects the browser.</summary>
    public string Browser { get; init; } = "Browser";

    /// <summary>What the "+" sheet's Browser and Remote Control rows say while the connection is off.</summary>
    public string StatusOff { get; init; } = "Off";

    /// <summary>What those rows say while the connection is being made.</summary>
    public string StatusConnecting { get; init; } = "Connecting…";

    /// <summary>What those rows say while the connection is up.</summary>
    public string StatusOn { get; init; } = "On";

    /// <summary>What those rows say when the connection failed (the Browser row also, when no browser answers).</summary>
    public string StatusError { get; init; } = "Error";

    /// <summary>The row of the "+" sheet, there while Remote Control is connected, that opens the session's link.</summary>
    public string OpenRemoteControlLink { get; init; } = "Open claude.ai/code";

    /// <summary>The button that takes an attachment off the message.</summary>
    public string RemoveAttachment { get; init; } = "Remove attachment";

    /// <summary>The button that opens the command menu.</summary>
    public string ShowCommands { get; init; } = "Show command menu (/)";

    /// <summary>The empty filter field of the command menu.</summary>
    public string FilterCommands { get; init; } = "Filter actions…";

    /// <summary>The command menu when the filter matches nothing.</summary>
    public string NoMatchingCommands { get; init; } = "No matching commands";

    /// <summary>The title of the command group in the menu.</summary>
    public string SlashCommands { get; init; } = "Slash Commands";

    /// <summary>What the context command does, in the menu.</summary>
    public string CommandContextDescription { get; init; } = "Show context usage";

    /// <summary>What the usage command does, in the menu.</summary>
    public string CommandUsageDescription { get; init; } = "Show plan usage limits";

    /// <summary>What the Remote Control command does, in the menu.</summary>
    public string CommandRemoteControlDescription { get; init; } = "Turn Remote Control on or off";

    /// <summary>What the browser command does, in the menu.</summary>
    public string CommandChromeDescription { get; init; } = "Connect or disconnect the browser";

    /// <summary>What the copy command does, in the menu.</summary>
    public string CommandCopyDescription { get; init; } = "Copy Claude's last response to clipboard (or /copy N for the Nth-latest)";

    /// <summary>The line after <c>/copy</c> worked; <c>{0}</c> is the count of characters and <c>{1}</c> the lines, <see cref="OneLine"/> or <see cref="Lines"/>.</summary>
    public string CopyDone { get; init; } = "Copied to clipboard ({0} characters, {1})";

    /// <summary>The line after <c>/copy</c> when no reply is finished yet.</summary>
    public string CopyNothing { get; init; } = "No assistant message to copy";

    /// <summary>The line after <c>/copy N</c> when there are fewer replies than N; <c>{0}</c> is how many there are.</summary>
    public string CopyOnly { get; init; } = "Only {0} assistant messages available to copy";

    /// <summary>The line after <c>/copy</c> with an argument that is not a number from 1; <c>{0}</c> is what was typed.</summary>
    public string CopyUsage { get; init; } = "Usage: /copy [N] where N is 1 (latest), 2, 3, … Got: {0}";

    /// <summary>The line after <c>/copy</c> when the clipboard refused the text.</summary>
    public string CopyFailed { get; init; } = "Could not copy to clipboard";

    /// <summary>The line after <c>/copy</c> typed while Claude works: the copy waits for the reply to end.</summary>
    public string CopyWaiting { get; init; } = "Copying once the reply finishes";

    /// <summary>The prompt cache is still valid; <c>{0}</c> is the minutes left.</summary>
    public string CacheWarm { get; init; } = "Prompt cache warm, about {0} min left.";

    /// <summary>The prompt cache has probably expired; <c>{0}</c> is how long Claude has been idle ("1h 15m").</summary>
    public string CacheExpired { get; init; } = "Prompt cache likely expired (idle {0}).";

    /// <summary>The cache does not cover a conversation that was compacted.</summary>
    public string CacheCompacted { get; init; } = "Prompt cache does not cover the compacted conversation.";

    /// <summary>The name of Remote Control.</summary>
    public string RemoteControl { get; init; } = "Remote Control";

    /// <summary>The banner while Remote Control is on.</summary>
    public string RemoteControlActive { get; init; } = "Remote Control is active · Click to open claude.ai/code · Run /remote-control to turn off";

    /// <summary>The banner while Remote Control connects.</summary>
    public string RemoteControlConnecting { get; init; } = "Connecting to claude.ai/code…";

    /// <summary>The banner when Remote Control fails; <c>{0}</c> is the reason.</summary>
    public string RemoteControlFailed { get; init; } = "Remote Control error: {0}";

    /// <summary>The Remote Control chip's tooltip when it fails; <c>{0}</c> is the reason.</summary>
    public string RemoteControlFailedHint { get; init; } = "Remote Control error: {0} · Run /remote-control to dismiss";

    /// <summary>The banner while the browser connects.</summary>
    public string BrowserConnecting { get; init; } = "Connecting to browser…";

    /// <summary>The banner once the browser is connected.</summary>
    public string BrowserConnected { get; init; } = "Browser connected";

    /// <summary>The banner when no browser answers.</summary>
    public string BrowserNoBrowsers { get; init; } = "No browsers are connected. Open Chrome with the Claude extension and make sure you're signed in to the same claude.ai account.";

    /// <summary>The banner when the browser fails; <c>{0}</c> is the reason.</summary>
    public string BrowserFailed { get; init; } = "Browser error: {0}";

    /// <summary>The button that disconnects the browser.</summary>
    public string DisconnectBrowser { get; init; } = "Disconnect browser";

    /// <summary>The title of the model menu.</summary>
    public string SelectModel { get; init; } = "Select a model";

    /// <summary>The title of the touch layout's model sheet.</summary>
    public string SelectModelTitle { get; init; } = "Select model";

    /// <summary>The label, in the touch layout's model sheet, over the models that are not among the main ones.</summary>
    public string OtherModels { get; init; } = "Other models";

    /// <summary>
    /// The touch layout's model sheet writes a model's description as this has it: the description the claude sent is the
    /// key, the text shown the value. A description it does not list is shown as it came. Empty in English, whose texts
    /// are the claude's own; the Portuguese list has the four descriptions the claude sends today.
    /// </summary>
    public IReadOnlyDictionary<string, string> ModelDescriptions { get; init; } = NoModelDescriptions;

    /// <summary>The model chip before the claude reports a model.</summary>
    public string Model { get; init; } = "Model";

    /// <summary>The model chip's tooltip.</summary>
    public string SwitchModel { get; init; } = "Switch model";

    /// <summary>The title of the effort choice.</summary>
    public string Effort { get; init; } = "Effort";

    /// <summary>The <c>low</c> effort.</summary>
    public string EffortLow { get; init; } = "Low";

    /// <summary>The <c>medium</c> effort.</summary>
    public string EffortMedium { get; init; } = "Medium";

    /// <summary>The <c>high</c> effort.</summary>
    public string EffortHigh { get; init; } = "High";

    /// <summary>The <c>xhigh</c> effort.</summary>
    public string EffortExtraHigh { get; init; } = "Extra high";

    /// <summary>The <c>max</c> effort.</summary>
    public string EffortMax { get; init; } = "Max";

    /// <summary>No effort chosen: the model decides.</summary>
    public string EffortAuto { get; init; } = "Auto";

    /// <summary>Shown when an image is too large to attach.</summary>
    public string ImageTooBig { get; init; } = "Image too large to attach";

    /// <summary>Shown when a message would carry too many images.</summary>
    public string TooManyImages { get; init; } = "Too many images in one message";

    /// <summary>Shown when a pasted or dropped file is not an image.</summary>
    public string NotAnImage { get; init; } = "Not an image";

    /// <summary>The kind of file the + button's file dialog shows.</summary>
    public string ImageFiles { get; init; } = "Images";

    /// <summary>Shown when a file is larger than the conversation's ceiling; <c>{0}</c> is the ceiling (<c>25 MB</c>).</summary>
    public string FileTooBig { get; init; } = "File is larger than {0}";

    /// <summary>Shown when a file is neither an image nor a PDF, text or CSV file.</summary>
    public string FileNotSupported { get; init; } = "Only PDF, text and CSV files";

    /// <summary>Shown when a message would carry too many files; <c>{0}</c> is how many it may carry.</summary>
    public string TooManyFiles { get; init; } = "Up to {0} files per message";

    /// <summary>On a file's chip while the host sends it, instead of its size; <c>{0}</c> is the whole percentage.</summary>
    public string FileUploading { get; init; } = "Sending {0}%";

    /// <summary>The kind of file the + button's file dialog shows: images and the files that may be attached.</summary>
    public string AttachableFiles { get; init; } = "Images, PDF, text and CSV";

    /// <summary>The title of the context usage panel.</summary>
    public string ContextTitle { get; init; } = "Context usage";

    /// <summary>The title of the plan usage panel.</summary>
    public string UsageTitle { get; init; } = "Usage";

    /// <summary>When a usage limit renews; <c>{0}</c> is the time.</summary>
    public string UsageResets { get; init; } = "Resets {0}";

    /// <summary>
    /// When a usage limit renews on another day: <c>{0}</c> is the day's short name (<see cref="DayNames"/>),
    /// <c>{1}</c> the time.
    /// </summary>
    public string UsageResetsDay { get; init; } = "{0} {1}";

    /// <summary>The seven short day names, Sunday first, separated by spaces.</summary>
    public string DayNames { get; init; } = "Sun Mon Tue Wed Thu Fri Sat";

    /// <summary>The <c>five_hour</c> usage limit.</summary>
    public string UsageFiveHour { get; init; } = "Current session (5h)";

    /// <summary>The <c>seven_day</c> usage limit.</summary>
    public string UsageSevenDay { get; init; } = "Weekly (7 days)";

    /// <summary>How much of a usage limit is used; <c>{0}</c> is the percentage.</summary>
    public string UsagePercent { get; init; } = "{0}%";

    /// <summary>The line of the context panel: <c>{0}</c> tokens used, <c>{1}</c> the window, <c>{2}</c> the percentage.</summary>
    public string ContextTotal { get; init; } = "{0} / {1} ({2}%)";

    /// <summary>The label, in the touch layout's usage sheet, over the card with the context window.</summary>
    public string ContextSection { get; init; } = "Context";

    /// <summary>The name of the card, in the usage sheet, with the context window.</summary>
    public string ContextWindow { get; init; } = "Context window";

    /// <summary>How much of the context window or of a plan limit is used, in the usage sheet; <c>{0}</c> is the whole percentage.</summary>
    public string PercentUsed { get; init; } = "{0}% used";

    /// <summary>The line under the context bar of the usage sheet: <c>{0}</c> the tokens used and <c>{1}</c> the window, both short ("150.6k of 1M tokens").</summary>
    public string TokensOfWindow { get; init; } = "{0} of {1} tokens";

    /// <summary>The label, in the usage sheet, over the plan's limits (shown when the host puts nothing of its own there).</summary>
    public string PlanUsage { get; init; } = "Plan usage";

    /// <summary>The name of the usage sheet's button that asks for the data again.</summary>
    public string Refresh { get; init; } = "Refresh";

    /// <summary>The usage panel when the claude has sent no rate limit data and the conversation is idle.</summary>
    public string UsageUnavailable { get; init; } = "No usage data yet";

    /// <summary>The context panel when the claude has sent no context data and the conversation is idle.</summary>
    public string ContextUnavailable { get; init; } = "No context data yet";

    /// <summary>A panel whose data the claude has not sent yet, while the conversation is busy.</summary>
    public string PanelLoading { get; init; } = "…";

    /// <summary>The chip of the reply box's bar: <c>{0}</c> is how many agents are at work.</summary>
    public string AgentsChip { get; init; } = "{0} agents";

    /// <summary>The context ring's tooltip, the extension's words: <c>{0}</c> is the percentage of the window still free.</summary>
    public string ContextRingTip { get; init; } = "{0}% of context remaining until auto-compact.";

    /// <summary>The tooltip's second line (the extension's says "Click to compact now."; a click here shows the usage).</summary>
    public string ContextRingHint { get; init; } = "Click to see the context usage.";

    /// <summary>The chip with one agent at work.</summary>
    public string AgentsChipOne { get; init; } = "1 agent";

    /// <summary>The touch layout's chip at the end of the list while no agent is at work: <c>{0}</c> is how many the conversation had.</summary>
    public string AgentsChipTouch { get; init; } = "Agents · {0}";

    /// <summary>The same chip while agents are at work: <c>{0}</c> is how many.</summary>
    public string AgentsChipTouchRunning { get; init; } = "Agents · {0} running";

    /// <summary>The title of the Agent map.</summary>
    public string AgentMapTitle { get; init; } = "Agent map";

    /// <summary>The line under the Agent map's title: <c>{0}</c> is how many agents the conversation had.</summary>
    public string AgentMapSubtitle { get; init; } = "{0} agents · click an agent for details";

    /// <summary>The line under the Agent map's title with one agent.</summary>
    public string AgentMapSubtitleOne { get; init; } = "1 agent · click an agent for details";

    /// <summary>The line under the Agent map's title in the touch layout: <c>{0}</c> is how many agents the conversation had.</summary>
    public string AgentMapSubtitleTouch { get; init; } = "{0} agents · tap an agent for details";

    /// <summary>The line under the Agent map's title in the touch layout, with one agent.</summary>
    public string AgentMapSubtitleTouchOne { get; init; } = "1 agent · tap an agent for details";

    /// <summary>The main agent's card in the Agent map.</summary>
    public string AgentMain { get; init; } = "Main agent";

    /// <summary>An agent's details: the main agent called it.</summary>
    public string AgentSpawnedByMain { get; init; } = "Spawned by the main agent";

    /// <summary>An agent's details: <c>{0}</c> is the description of the agent that called it.</summary>
    public string AgentSpawnedBy { get; init; } = "Spawned by {0}";

    /// <summary>An agent at work.</summary>
    public string AgentRunning { get; init; } = "Running";

    /// <summary>An agent that finished.</summary>
    public string AgentFinished { get; init; } = "Finished";

    /// <summary>An agent that failed.</summary>
    public string AgentFailed { get; init; } = "Failed";

    /// <summary>An agent stopped before its end.</summary>
    public string AgentStopped { get; init; } = "Stopped";

    /// <summary>An agent that runs while the conversation goes on.</summary>
    public string AgentBackground { get; init; } = "background";

    /// <summary>An agent's details, before its answer.</summary>
    public string AgentNoResult { get; init; } = "No result yet";

    /// <summary>The line under a background agent's row while it works and nothing tells what it is doing.</summary>
    public string AgentWorkingInBackground { get; init; } = "Working in the background";

    /// <summary>The same line with what it is doing: <c>{0}</c> is its last activity, cut at 120 characters.</summary>
    public string AgentWorkingInBackgroundOn { get; init; } = "Working in the background: {0}";

    /// <summary>The line under a background agent's row while one of its requests waits in the card below.</summary>
    public string AgentWaitingForPermission { get; init; } = "Waiting for your permission: see the request below";

    /// <summary>The same line when an agent it started waits for a request too.</summary>
    public string AgentWaitingAlongWithNested { get; init; } = "Waiting for your permission, and so is an agent it started: see the request below";

    /// <summary>The line under a background agent's row when it failed: <c>{0}</c> is what it said, cut at 120 characters (<see cref="AgentFailed"/> without it).</summary>
    public string AgentFailedWith { get; init; } = "Failed: {0}";

    /// <summary>Added (after " · ") to a background agent's line when an agent it started waits for a request.</summary>
    public string AgentNestedWaiting { get; init; } = "an agent it started is waiting for your permission: see the request below";

    /// <summary>Added (after " · ") to a background agent's line that is over while an agent it started still works.</summary>
    public string AgentNestedWorking { get; init; } = "an agent it started is still working";

    /// <summary>Added (after " · ") to a background agent's line when an agent it started failed.</summary>
    public string AgentNestedFailed { get; init; } = "an agent it started failed";

    /// <summary>Added (after " · ") to a background agent's line when an agent it started was stopped.</summary>
    public string AgentNestedStopped { get; init; } = "an agent it started was stopped";

    /// <summary>The end (after " · ") of every background agent's line.</summary>
    public string AgentTasksHint { get; init; } = "type /tasks to see its transcript";

    /// <summary>The tokens an agent used: <c>{0}</c> is the count, short ("26.3k").</summary>
    public string AgentTokens { get; init; } = "{0} tokens";

    /// <summary>The main agent's context: <c>{0}</c> is the count, short ("249.5k").</summary>
    public string AgentContextTokens { get; init; } = "{0} tokens in context";

    /// <summary>The name of a back button.</summary>
    public string Back { get; init; } = "Back";

    /// <summary>The name of a close button.</summary>
    public string Close { get; init; } = "Close";

    /// <summary>The name of the button that dismisses a banner.</summary>
    public string Dismiss { get; init; } = "Dismiss";

    /// <summary>The name of the button under a finished turn that copies Claude's answer (the touch layout's).</summary>
    public string CopyAnswer { get; init; } = "Copy";

    /// <summary>The same button's name for a moment after it copied.</summary>
    public string Copied { get; init; } = "Copied";

    /// <summary>The tooltip of the button on a command's row that copies the command.</summary>
    public string CopyCode { get; init; } = "Copy code";

    /// <summary>The name of that button for a screen reader (it reads <see cref="Copied"/> for a moment after a copy).</summary>
    public string CopyCodeToClipboard { get; init; } = "Copy code to clipboard";

    /// <summary>The tooltip of the button under a finished turn that copies Claude's answer (the desktop's row of actions).</summary>
    public string CopyResponse { get; init; } = "Copy response";

    /// <summary>The name of that button for a screen reader (it reads <see cref="Copied"/> for a moment after a copy).</summary>
    public string CopyResponseToClipboard { get; init; } = "Copy response to clipboard";

    /// <summary>The bookmark button under a finished turn, while its reply is not bookmarked.</summary>
    public string BookmarkResponse { get; init; } = "Bookmark response";

    /// <summary>The same button once the reply is bookmarked.</summary>
    public string RemoveBookmark { get; init; } = "Remove bookmark";

    /// <summary>The thumb up under a finished turn (while the host takes feedback).</summary>
    public string GoodResponse { get; init; } = "Good response";

    /// <summary>The thumb down under a finished turn.</summary>
    public string BadResponse { get; init; } = "Bad response";

    /// <summary>The link beside the thumbs before a rating (while the host opens links).</summary>
    public string FeedbackLearnHow { get; init; } = "Learn how we use your feedback";

    /// <summary>Beside the thumbs after a rating.</summary>
    public string FeedbackThanks { get; init; } = "Thanks for your feedback";

    /// <summary>The link after <see cref="FeedbackThanks"/> (while the host opens links).</summary>
    public string FeedbackLearnHowShort { get; init; } = "Learn how we use it";

    /// <summary>The title of the box that opens with the thumb down.</summary>
    public string FeedbackTitle { get; init; } = "What went wrong?";

    /// <summary>The placeholder of that box's field.</summary>
    public string FeedbackPlaceholder { get; init; } = "Tell us more (optional)";

    /// <summary>The button that closes that box without sending what it says.</summary>
    public string FeedbackSkip { get; init; } = "Skip";

    /// <summary>The button that sends what the box says (Ctrl+Enter too).</summary>
    public string FeedbackSend { get; init; } = "Send feedback";

    /// <summary>The button inside a long prompt folded at 60 px, which shows it whole.</summary>
    public string ShowMore { get; init; } = "Show more";

    /// <summary>The button under a long prompt shown whole, which folds it again.</summary>
    public string ShowLess { get; init; } = "Show less";

    public string ThousandsSeparator { get; init; } = ",";

    public string DecimalSeparator { get; init; } = ".";

    /// <summary>Text for a <see cref="NoticeItem"/>.</summary>
    internal string Notice(NoticeKind kind) => kind switch
    {
        NoticeKind.Interrupted => Interrupted,
        NoticeKind.ToolInterrupted => ToolInterrupted,
        NoticeKind.Compacted => Compacted,
        NoticeKind.ApiRetry => ApiRetry,
        NoticeKind.TurnFailed => TurnFailed,
        _ => "",
    };

    /// <summary>A whole number with <see cref="ThousandsSeparator"/>: 38557 → "38,557".</summary>
    internal string FormatNumber(long value)
    {
        var digits = value < 0
            ? (-(decimal)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);
        var text = new StringBuilder(digits.Length + digits.Length / 3 + 1);
        if (value < 0) text.Append('-');
        var head = digits.Length % 3;
        if (head == 0) head = 3;
        text.Append(digits, 0, head);
        for (var i = head; i < digits.Length; i += 3)
            text.Append(ThousandsSeparator).Append(digits, i, 3);
        return text.ToString();
    }

    /// <summary>Below 60 s, seconds with one decimal ("4.2 s"); from 60 s on, "m:ss".</summary>
    internal string FormatDuration(long milliseconds)
    {
        if (milliseconds < 0) milliseconds = 0;
        var tenths = (long)Math.Round(milliseconds / 100.0, MidpointRounding.AwayFromZero);
        if (tenths < 600) return FormatNumber(tenths / 10) + DecimalSeparator + (tenths % 10) + " s";
        var seconds = (long)Math.Round(milliseconds / 1000.0, MidpointRounding.AwayFromZero);
        return (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" +
               (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>A number with two decimals: 0.0839 → "0.08".</summary>
    internal string FormatMoney(double value)
    {
        var cents = (long)Math.Round(Math.Abs(value) * 100, MidpointRounding.AwayFromZero);
        var sign = value < 0 && cents > 0 ? "-" : "";
        return sign + FormatNumber(cents / 100) + DecimalSeparator + (cents % 100).ToString("00", CultureInfo.InvariantCulture);
    }

    internal string FormatMoreLines(int lines) => MoreLines.Replace("{0}", FormatNumber(lines));

    /// <summary>
    /// A file size: below 1 MB in whole KB, rounded up ("1 KB" for a few bytes); from 1 MB on in MB with one decimal,
    /// without a ".0" ("1.5 MB", "25 MB"). 1 KB is 1024 bytes.
    /// </summary>
    internal string FormatFileSize(long bytes)
    {
        if (bytes < 0) bytes = 0;
        const long Kb = 1024;
        const long Mb = 1024 * 1024;
        var kilobytes = (bytes + Kb - 1) / Kb;
        if (kilobytes < 1024) return FormatNumber(kilobytes) + " KB"; // 1024 KB once rounded up is 1 MB
        var tenths = (long)Math.Round(bytes * 10.0 / Mb, MidpointRounding.AwayFromZero);
        var whole = FormatNumber(tenths / 10);
        return (tenths % 10 == 0 ? whole : whole + DecimalSeparator + (tenths % 10)) + " MB";
    }

    internal string FormatFileTooBig(long maxBytes) => FileTooBig.Replace("{0}", FormatFileSize(maxBytes));

    internal string FormatTooManyFiles(int maxFiles) => TooManyFiles.Replace("{0}", FormatNumber(maxFiles));

    /// <summary>The upload's whole percentage, from 0 to 100.</summary>
    internal string FormatFileUploading(double progress)
    {
        var percent = double.IsNaN(progress) ? 0 : Math.Max(0, Math.Min(100, Math.Round(progress * 100, MidpointRounding.AwayFromZero)));
        return FileUploading.Replace("{0}", ((int)percent).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>A time in local time, with <see cref="TimeFormat"/>; a format .NET refuses gives "HH:mm".</summary>
    internal string FormatTime(DateTimeOffset time)
    {
        var local = time.ToLocalTime();
        try
        {
            return local.ToString(TimeFormat, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>"Thought for 3s": the seconds rounded like the extension (half up).</summary>
    internal string FormatThoughtFor(long milliseconds)
    {
        var seconds = (long)Math.Floor(Math.Max(0, milliseconds) / 1000.0 + 0.5);
        return ThoughtFor.Replace("{0}", seconds.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>"47 lines", "1 line".</summary>
    internal string FormatLines(int lines) => lines == 1 ? OneLine : Lines.Replace("{0}", FormatNumber(lines));

    /// <summary>The line after a copy: the characters (UTF-16 units) and the lines (the line feeds plus one) of <paramref name="text"/>.</summary>
    internal string FormatCopied(string text)
    {
        var lines = 1;
        foreach (var c in text)
        {
            if (c == '\n') lines++;
        }
        return CopyDone.Replace("{0}", FormatNumber(text.Length)).Replace("{1}", FormatLines(lines));
    }

    /// <summary>The line after <c>/copy N</c> when only <paramref name="available"/> replies can be copied.</summary>
    internal string FormatCopyOnly(int available) => CopyOnly.Replace("{0}", FormatNumber(available));

    /// <summary>The line after <c>/copy</c> with the argument <paramref name="got"/>, which is not a number from 1.</summary>
    internal string FormatCopyUsage(string got) => CopyUsage.Replace("{0}", got);

    /// <summary>The line under a Glob: "No files found", "Found 1 file", "Found 3 files".</summary>
    internal string FormatFilesFound(int files) =>
        files <= 0 ? GlobNone : files == 1 ? GlobOne : GlobMany.Replace("{0}", FormatNumber(files));

    /// <summary>The line under a Grep: "No matches found", "1 line of output", "3 lines of output".</summary>
    internal string FormatLinesOfOutput(int lines) =>
        lines <= 0 ? GrepNone : lines == 1 ? GrepOne : GrepMany.Replace("{0}", FormatNumber(lines));

    /// <summary>The box of a Web Fetch that worked.</summary>
    internal string FormatFetchedFrom(string url) => FetchedFrom.Replace("{0}", url);

    /// <summary>The line a Claude in Chrome action leaves under its name ("Navigation completed", "Page read"…); null for an action that has none.</summary>
    internal string? ChromeLine(string action) => action switch
    {
        "navigate" => ChromeNavigated,
        "tabs_create" => ChromeTabCreated,
        "tabs_context" => ChromeTabsRead,
        "form_input" => ChromeInputCompleted,
        "resize_window" => ChromeWindowResized,
        "find" => ChromeSearchCompleted,
        "gif_creator" => ChromeGifCompleted,
        "read_console_messages" => ChromeConsoleRetrieved,
        "read_network_requests" => ChromeNetworkRetrieved,
        "javascript_tool" => ChromeScriptExecuted,
        "read_page" => ChromePageRead,
        "get_page_text" => ChromeTextRetrieved,
        "upload_image" => ChromeImageUploaded,
        "update_plan" => ChromePlanUpdated,
        _ => null,
    };

    /// <summary>The line under a Web Search, like "Allowed: a.com, b.com · Blocked: c.com", each part only when its list has domains; null when neither has.</summary>
    internal string? FormatWebDomains(IReadOnlyList<string> allowed, IReadOnlyList<string> blocked)
    {
        var parts = new List<string>();
        if (allowed.Count > 0) parts.Add(WebAllowed + " " + string.Join(", ", allowed));
        if (blocked.Count > 0) parts.Add(WebBlocked + " " + string.Join(", ", blocked));
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>The line under an edit: "Modified", "Added 2 lines", "Added 2 lines, removed 1 line"…</summary>
    internal string FormatEditChange(LineChange change)
    {
        if (change.Added > 0 && change.Removed > 0)
            return EditAddedRemoved.Replace("{0}", FormatLines(change.Added)).Replace("{1}", FormatLines(change.Removed));
        if (change.Added > 0) return EditAdded.Replace("{0}", FormatLines(change.Added));
        if (change.Removed > 0) return EditRemoved.Replace("{0}", FormatLines(change.Removed));
        return EditModified;
    }

    /// <summary>"(lines 2-61)" or "(from line 2)"; line numbers go without separators, like in an editor.</summary>
    internal string FormatReadRange(LineRange range) => range.To is int to
        ? ReadLines.Replace("{0}", Plain(range.From)).Replace("{1}", Plain(to))
        : ReadFromLine.Replace("{0}", Plain(range.From));

    private static string Plain(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The label of a permission mode; a mode without a label keeps its name.</summary>
    internal string ModeLabel(string mode) => mode switch
    {
        "plan" => ModePlan,
        "default" => ModeDefault,
        "acceptEdits" => ModeAcceptEdits,
        "auto" => ModeAuto,
        _ => mode ?? "",
    };

    /// <summary>The label of an effort level; no level is <see cref="EffortAuto"/>, an unknown one keeps its name.</summary>
    internal string EffortLabel(string? level) => level switch
    {
        null => EffortAuto,
        "low" => EffortLow,
        "medium" => EffortMedium,
        "high" => EffortHigh,
        "xhigh" => EffortExtraHigh,
        "max" => EffortMax,
        _ => level,
    };

    /// <summary>What the "+" sheet's Browser row says: the connection's state; a browser that does not answer is an error too.</summary>
    internal string BrowserStatusLabel(ChatBrowserStatus status) => status switch
    {
        ChatBrowserStatus.Connecting => StatusConnecting,
        ChatBrowserStatus.Connected => StatusOn,
        ChatBrowserStatus.NoBrowser or ChatBrowserStatus.Error => StatusError,
        _ => StatusOff,
    };

    /// <summary>What the "+" sheet's Remote Control row says: the connection's state.</summary>
    internal string RemoteControlStatusLabel(ChatRemoteControlStatus status) => status switch
    {
        ChatRemoteControlStatus.Connecting => StatusConnecting,
        ChatRemoteControlStatus.Connected => StatusOn,
        ChatRemoteControlStatus.Error => StatusError,
        _ => StatusOff,
    };

    /// <summary>A model's description as the touch layout's sheet shows it: <see cref="ModelDescriptions"/> has the text for it, else it stays as the claude sent it.</summary>
    internal string ModelDescription(string description) =>
        description != null && ModelDescriptions != null && ModelDescriptions.TryGetValue(description, out var text) ? text : description ?? "";

    /// <summary>Minutes left in the cache: 59 → "59m".</summary>
    internal string FormatCacheMinutes(int minutes) => Plain(Math.Max(0, minutes)) + "m";

    /// <summary>An idle time, compact: "0m", "12m", "1h 15m", "2d 3h".</summary>
    internal string FormatIdle(TimeSpan idle)
    {
        var minutes = (long)Math.Max(0, idle.TotalMinutes);
        var culture = CultureInfo.InvariantCulture;
        if (minutes >= 1440) return (minutes / 1440).ToString(culture) + "d " + (minutes % 1440 / 60).ToString(culture) + "h";
        if (minutes >= 60) return (minutes / 60).ToString(culture) + "h " + (minutes % 60).ToString(culture) + "m";
        return minutes.ToString(culture) + "m";
    }

    /// <summary><see cref="CacheWarm"/> with the minutes left.</summary>
    internal string FormatCacheWarm(int minutes) => CacheWarm.Replace("{0}", Plain(Math.Max(0, minutes)));

    /// <summary><see cref="CacheExpired"/> with the idle time (<see cref="FormatIdle"/>).</summary>
    internal string FormatCacheExpired(TimeSpan idle) => CacheExpired.Replace("{0}", FormatIdle(idle));

    internal string FormatRemoteControlFailed(string? reason) => RemoteControlFailed.Replace("{0}", reason ?? "");

    internal string FormatRemoteControlFailedHint(string? reason) => RemoteControlFailedHint.Replace("{0}", reason ?? "");

    internal string FormatBrowserFailed(string? reason) => BrowserFailed.Replace("{0}", reason ?? "");

    internal string FormatUseTool(string toolName) => PermissionUseTool.Replace("{0}", toolName ?? "");

    /// <summary><see cref="PermissionEditFileQuestion"/> with the file's name: "Edit OrderService.cs?".</summary>
    internal string FormatEditFileQuestion(string fileName) => PermissionEditFileQuestion.Replace("{0}", fileName ?? "");

    /// <summary><see cref="PermissionUseToolQuestion"/> with the tool's name: "Use Fetch?".</summary>
    internal string FormatUseToolQuestion(string toolName) => PermissionUseToolQuestion.Replace("{0}", toolName ?? "");

    internal string FormatMorePending(int count) => MorePending.Replace("{0}", FormatNumber(count));

    /// <summary><see cref="PlanComments"/> with the count: "Comments (2)".</summary>
    internal string FormatPlanComments(int count) => PlanComments.Replace("{0}", FormatNumber(count));

    /// <summary>"1 comment will be included as feedback", or <see cref="PlanCommentsIncluded"/> with the count.</summary>
    internal string FormatPlanCommentsIncluded(int count) =>
        count == 1 ? PlanOneCommentIncluded : PlanCommentsIncluded.Replace("{0}", FormatNumber(count));

    /// <summary><see cref="AllowAlwaysMoreRules"/> with the count: "2 more".</summary>
    internal string FormatAllowAlwaysMore(int count) => AllowAlwaysMoreRules.Replace("{0}", FormatNumber(count));

    /// <summary><see cref="PermissionSkillArguments"/> with what the skill is given: "Arguments: 12".</summary>
    internal string FormatSkillArguments(string arguments) => PermissionSkillArguments.Replace("{0}", arguments ?? "");

    /// <summary>
    /// The domains of a web search: "Allowed: a.com, b.com · Blocked: c.com", either part alone, or empty when it gives none.
    /// </summary>
    internal string FormatSearchDomains(IReadOnlyList<string> allowed, IReadOnlyList<string> blocked)
    {
        var parts = new List<string>();
        if (allowed != null && allowed.Count > 0) parts.Add(PermissionAllowedDomains.Replace("{0}", string.Join(", ", allowed)));
        if (blocked != null && blocked.Count > 0) parts.Add(PermissionBlockedDomains.Replace("{0}", string.Join(", ", blocked)));
        return string.Join(Separator, parts);
    }

    /// <summary><see cref="ContextTotal"/>: "36,174 / 200,000 (18%)".</summary>
    internal string FormatContextTotal(ContextUsage usage) => ContextTotal
        .Replace("{0}", FormatNumber(usage.TotalTokens))
        .Replace("{1}", FormatNumber(usage.MaxTokens))
        .Replace("{2}", Plain(usage.Percentage));

    /// <summary>The ring's tooltip for a percentage used (0 to 100): the free share, then the hint.</summary>
    internal string FormatContextRingTip(double percentUsed) =>
        ContextRingTip.Replace("{0}", Plain((int)Math.Round(100 - percentUsed))) + "\n" + ContextRingHint;

    /// <summary><see cref="AgentsChip"/>: "0 agents", "1 agent", "3 agents".</summary>
    internal string FormatAgentsChip(int count) =>
        count == 1 ? AgentsChipOne : AgentsChip.Replace("{0}", FormatNumber(count));

    /// <summary>
    /// The touch layout's chip: <see cref="AgentsChipTouchRunning"/> with the agents at work while any is ("Agents · 2
    /// running"), else <see cref="AgentsChipTouch"/> with all of them ("Agents · 5").
    /// </summary>
    internal string FormatAgentsChipTouch(int running, int total) => running > 0
        ? AgentsChipTouchRunning.Replace("{0}", FormatNumber(running))
        : AgentsChipTouch.Replace("{0}", FormatNumber(total));

    /// <summary>
    /// <see cref="AgentMapSubtitle"/>: "73 agents · click an agent for details"; in the touch layout
    /// <see cref="AgentMapSubtitleTouch"/>: "73 agents · tap an agent for details".
    /// </summary>
    internal string FormatAgentMapSubtitle(int count, bool touch = false) => touch
        ? count == 1 ? AgentMapSubtitleTouchOne : AgentMapSubtitleTouch.Replace("{0}", FormatNumber(count))
        : count == 1 ? AgentMapSubtitleOne : AgentMapSubtitle.Replace("{0}", FormatNumber(count));

    /// <summary>
    /// A token count, short, like the extension's: as is below 1,000 ("800"), then thousands with one decimal up to
    /// "999.9k", then millions ("1.2M").
    /// </summary>
    internal string FormatShortTokens(long tokens)
    {
        if (tokens < 1000) return Plain(Math.Max(0, tokens));
        var tenths = (long)Math.Round(tokens / 100.0, MidpointRounding.AwayFromZero);
        if (tenths < 10_000) return Plain(tenths / 10) + DecimalSeparator + Plain(tenths % 10) + "k";
        tenths = (long)Math.Round(tokens / 100_000.0, MidpointRounding.AwayFromZero);
        return FormatNumber(tenths / 10) + DecimalSeparator + Plain(tenths % 10) + "M";
    }

    private static string Plain(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// <see cref="FormatShortTokens"/> without a decimal that is zero: "1.0M" is "1M", "150.0k" is "150k", "150.6k" stays; the
    /// touch layout's usage sheet writes the window this way.
    /// </summary>
    internal string FormatShortTokensTrimmed(long tokens)
    {
        var text = FormatShortTokens(tokens);
        if (string.IsNullOrEmpty(DecimalSeparator) || text.Length < 2) return text;
        var unit = text[text.Length - 1];                       // k or M, none below 1,000
        if (unit != 'k' && unit != 'M') return text;
        var zero = DecimalSeparator + "0";
        var cut = text.Length - 1 - zero.Length;
        return cut > 0 && string.CompareOrdinal(text, cut, zero, 0, zero.Length) == 0 ? text.Substring(0, cut) + unit : text;
    }

    /// <summary><see cref="PercentUsed"/> of a share from 0 to 100, to the whole number: 15.06 → "15% used".</summary>
    internal string FormatPercentUsed(double percent) =>
        PercentUsed.Replace("{0}", Plain(percent > 0 ? (long)Math.Round(percent, MidpointRounding.AwayFromZero) : 0));

    /// <summary><see cref="TokensOfWindow"/> with both counts short and trimmed: "150.6k of 1M tokens".</summary>
    internal string FormatTokensOfWindow(long tokens, long max) => TokensOfWindow
        .Replace("{0}", FormatShortTokensTrimmed(tokens))
        .Replace("{1}", FormatShortTokensTrimmed(max));

    /// <summary>A share of the window, from 0 to 1, as a percentage with one decimal: 0.1134 → "11.3%".</summary>
    internal string FormatSharePercent(double share)
    {
        var tenths = share > 0 ? (long)Math.Round(share * 1000, MidpointRounding.AwayFromZero) : 0;
        return Plain(tenths / 10) + DecimalSeparator + Plain(tenths % 10) + "%";
    }

    /// <summary>How long an agent ran, like the extension's: "44s", "7m 14s" (whole seconds, cut down).</summary>
    internal static string FormatAgentTime(long milliseconds)
    {
        var seconds = Math.Max(0, milliseconds) / 1000;
        return seconds < 60 ? Plain(seconds) + "s" : Plain(seconds / 60) + "m " + Plain(seconds % 60) + "s";
    }

    internal string FormatAgentTokens(long tokens) => AgentTokens.Replace("{0}", FormatShortTokens(tokens));

    internal string FormatAgentContextTokens(long tokens) => AgentContextTokens.Replace("{0}", FormatShortTokens(tokens));

    internal string FormatAgentSpawnedBy(string caller) => AgentSpawnedBy.Replace("{0}", caller ?? "");

    /// <summary>The word for an agent's state.</summary>
    internal string AgentStatusLabel(AgentStatus status) => status switch
    {
        AgentStatus.Running => AgentRunning,
        AgentStatus.Completed => AgentFinished,
        AgentStatus.Failed => AgentFailed,
        _ => AgentStopped,
    };

    /// <summary>Most characters of a background agent's activity or error on its line; past it, 119 and "…".</summary>
    private const int MaxAgentLineChars = 120;

    /// <summary>
    /// The line under a background agent's row, like the extension's: what it is doing (its <paramref name="activity"/>, left out
    /// when it is the <paramref name="description"/>), that it waits for a request, or how it ended (a failure with its
    /// <paramref name="error"/>); then what the agents it started do (<paramref name="nested"/>); always ending with
    /// <see cref="AgentTasksHint"/>. The activity and the error are trimmed and cut at 120 characters.
    /// </summary>
    internal string FormatBackgroundAgent(AgentStatus status, bool waiting, string? activity, string? error, string description, AgentNesting nested)
    {
        var doing = AgentLineText(activity);
        if (doing == (description ?? "").Trim()) doing = "";
        var why = AgentLineText(error);
        var nestedWaiting = (nested & AgentNesting.Waiting) != 0;
        string head;
        if (waiting) head = nestedWaiting ? AgentWaitingAlongWithNested : AgentWaitingForPermission;
        else if (status == AgentStatus.Running) head = doing.Length > 0 ? AgentWorkingInBackgroundOn.Replace("{0}", doing) : AgentWorkingInBackground;
        else if (status == AgentStatus.Failed && why.Length > 0) head = AgentFailedWith.Replace("{0}", why);
        else head = AgentStatusLabel(status);

        var line = new StringBuilder(head);
        if (!waiting && nestedWaiting) line.Append(Separator).Append(AgentNestedWaiting);
        else if (!waiting && (nested & AgentNesting.Working) != 0 && status != AgentStatus.Running) line.Append(Separator).Append(AgentNestedWorking);
        if ((nested & AgentNesting.Failed) != 0) line.Append(Separator).Append(AgentNestedFailed);
        if ((nested & AgentNesting.Stopped) != 0 && status != AgentStatus.Stopped) line.Append(Separator).Append(AgentNestedStopped);
        return line.Append(Separator).Append(AgentTasksHint).ToString();
    }

    private static readonly System.Text.RegularExpressions.Regex LineBreaks = new System.Text.RegularExpressions.Regex(@"\s*[\r\n]+\s*");

    /// <summary>An activity or an error for the agent's line: its line breaks become a space, it is trimmed, and past 120 characters its first 119 and "…".</summary>
    private static string AgentLineText(string? text)
    {
        var trimmed = LineBreaks.Replace((text ?? "").Trim(), " ");
        return trimmed.Length > MaxAgentLineChars ? Controls.ChatText.Clip(trimmed, MaxAgentLineChars - 1) : trimmed;
    }

    /// <summary>The label of a usage limit; one without a label keeps its name.</summary>
    internal string UsageWindowLabel(string name) => name switch
    {
        "five_hour" => UsageFiveHour,
        "seven_day" => UsageSevenDay,
        _ => name ?? "",
    };

    /// <summary><see cref="UsagePercent"/> of a use from 0 to 1, rounded: 0.12 → "12%".</summary>
    internal string FormatUsagePercent(double utilization)
    {
        var percent = (long)Math.Round(Math.Max(0, utilization) * 100, MidpointRounding.AwayFromZero);
        return UsagePercent.Replace("{0}", percent.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// <see cref="UsageResets"/> in local time: the time alone when it is on the day of <paramref name="now"/>, else
    /// <see cref="UsageResetsDay"/> with the day's short name.
    /// </summary>
    internal string FormatUsageResets(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        var time = FormatTime(at);
        var when = local.Date == now.ToLocalTime().Date
            ? time
            : UsageResetsDay.Replace("{0}", DayName(local.DayOfWeek)).Replace("{1}", time);
        return UsageResets.Replace("{0}", when);
    }

    /// <summary>The day's short name from <see cref="DayNames"/>; the English one when that is not seven names.</summary>
    private string DayName(DayOfWeek day)
    {
        var names = (DayNames ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (names.Length != 7) names = English.DayNames.Split(' ');
        return names[(int)day];
    }

    /// <summary><see cref="Footer"/> with what the turn reported; empty when it reported nothing.</summary>
    internal string FormatFooter(TurnFooterItem footer)
    {
        if (footer == null) throw new ArgumentNullException(nameof(footer));
        var values = new[]
        {
            footer.DurationMs is long ms ? FormatDuration(ms) : null,
            footer.InputTokens is long input ? FormatNumber(input) : null,
            footer.OutputTokens is long output ? FormatNumber(output) : null,
            footer.CostUsd is double cost ? FormatMoney(cost) : null,
        };
        if (Array.TrueForAll(values, v => v == null)) return "";

        var kept = new List<string>();
        foreach (var part in Footer.Split(new[] { Separator }, StringSplitOptions.None))
        {
            var text = part;
            var known = true;
            for (var i = 0; i < values.Length; i++)
            {
                var placeholder = "{" + i + "}";
                if (text.IndexOf(placeholder, StringComparison.Ordinal) < 0) continue;
                if (values[i] == null)
                {
                    known = false;
                    break;
                }
                text = text.Replace(placeholder, values[i]);
            }
            if (known) kept.Add(text);
        }
        return string.Join(Separator, kept);
    }
}
