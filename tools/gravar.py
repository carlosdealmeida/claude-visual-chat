"""Records real `claude -p` stream-json sessions and sanitizes them into test fixtures.

Usage: python -I tools/gravar.py <scenario>...|--todos|--re-higienizar|--re-extrair-historico
       [--saida tests/fixtures]

The sanitizer functions are pure (see test_gravar.py). Messages go to stderr; the
original `system/init` content is never printed.
"""
import argparse
import base64
import glob
import json
import os
import queue
import re
import shutil
import struct
import subprocess
import sys
import threading
import time
import zlib
from datetime import datetime

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CENARIOS = os.path.join(RAIZ, "tools", "cenarios.json")
PRAZO_S = 180

INIT_CAMPOS = ("type", "subtype", "session_id", "model", "cwd", "permissionMode",
               "claude_code_version", "tools")
TRANSCRIPT_CAMPOS = ("type", "subtype", "uuid", "parentUuid", "isSidechain", "isMeta",
                     "isCompactSummary", "timestamp", "sessionId", "message", "toolUseResult",
                     "toolDenialKind", "thinkingDurationMs")
TRANSCRIPT_TIPOS = ("user", "assistant", "system")

# Slash commands that may stay in a recording: the built-ins of Claude Code and the demo's own.
COMANDOS_PUBLICOS = ("compact", "context", "usage", "cost", "model", "effort", "hello")
INITIALIZE_CAMPOS = ("commands", "models")
SETTINGS_APPLIED_CAMPOS = ("model", "effort")
CONTEXTO_CAMPOS = ("totalTokens", "maxTokens", "percentage")
CONTEXTO_CATEGORIA_CAMPOS = ("name", "tokens", "kind")

DEMO_PASTA = r"C:\demo\app"
DEMO_HOME = r"C:\Users\demo"
DEMO_USUARIO = "demo"


def log(msg):
    print(msg, file=sys.stderr, flush=True)


# ---------------------------------------------------------------- sanitizer

def _slug(caminho):
    """Claude's folder name for a path: every character that is not a letter or digit becomes `-`."""
    return re.sub(r"[^A-Za-z0-9]", "-", caminho.rstrip("\\/"))


def _formas(caminho):
    """Spellings of a Windows path: backslash, doubled backslash, slash, MSYS and Claude's slug."""
    limpo = caminho.rstrip("\\/")
    barra = limpo.replace("/", "\\")
    formas = [barra, barra.replace("\\", "\\\\"), barra.replace("\\", "/")]
    m = re.match(r"^([A-Za-z]):\\(.*)$", barra)
    if m:
        formas.append("/" + m.group(1).lower() + "/" + m.group(2).replace("\\", "/"))
    else:
        formas.append(formas[2])
    formas.append(_slug(barra))
    return formas


def montar_trocas(pasta, home, usuario):
    """Replacement pairs (source, target, whole_word), longest source first."""
    pares = []
    for origem, destino in ((pasta, DEMO_PASTA), (home, DEMO_HOME)):
        for fo, fd in zip(_formas(origem), _formas(destino)):
            pares.append((fo, fd, False))
    if usuario:
        pares.append((usuario, DEMO_USUARIO, bool(re.fullmatch(r"\w+", usuario))))
    pares.sort(key=lambda p: len(p[0]), reverse=True)
    return pares


def _trocar_texto(texto, troca):
    for origem, destino, palavra in troca:
        padrao = re.escape(origem)
        if palavra:
            padrao = r"\b" + padrao + r"\b"
        texto = re.sub(padrao, lambda _m, d=destino: d, texto, flags=re.IGNORECASE)
    return texto


def _trocar(valor, troca):
    if isinstance(valor, str):
        return _trocar_texto(valor, troca)
    if isinstance(valor, list):
        return [_trocar(v, troca) for v in valor]
    if isinstance(valor, dict):
        return {_trocar_texto(k, troca): _trocar(v, troca) for k, v in valor.items()}
    return valor


def _com_resposta(obj, interno):
    novo = dict(obj)
    novo["response"] = dict(obj["response"])
    novo["response"]["response"] = interno
    return novo


def _comandos_publicos(comandos):
    return [c for c in comandos or [] if isinstance(c, dict) and c.get("name") in COMANDOS_PUBLICOS]


def _higienizar_initialize(interno):
    """Keep the public slash commands and the models; account, agents and output styles are the user's."""
    novo = {k: interno[k] for k in INITIALIZE_CAMPOS if k in interno}
    if "commands" in novo:
        novo["commands"] = _comandos_publicos(novo["commands"])
    return novo


def _higienizar_settings(interno):
    """Keep only what was applied (model, effort); `effective` carries env and permissions."""
    aplicado = interno.get("applied")
    aplicado = aplicado if isinstance(aplicado, dict) else {}
    return {"applied": {k: aplicado[k] for k in SETTINGS_APPLIED_CAMPOS if k in aplicado}}


def _higienizar_contexto(interno):
    novo = {}
    if isinstance(interno.get("categories"), list):
        novo["categories"] = [{k: c[k] for k in CONTEXTO_CATEGORIA_CAMPOS if k in c}
                              for c in interno["categories"] if isinstance(c, dict)]
    novo.update({k: interno[k] for k in CONTEXTO_CAMPOS if k in interno})
    return novo


def higienizar_stream(obj, troca):
    if not isinstance(obj, dict):
        return None
    tipo = obj.get("type")
    if tipo == "rate_limit_event":
        return None
    if tipo == "control_response":
        resp = obj.get("response")
        resp = resp if isinstance(resp, dict) else {}
        interno = resp.get("response")
        if isinstance(interno, dict):
            if "commands" in interno or "models" in interno or "account" in interno:
                return _trocar(_com_resposta(obj, _higienizar_initialize(interno)), troca)
            if "effective" in interno or "applied" in interno:
                return _trocar(_com_resposta(obj, _higienizar_settings(interno)), troca)
            if "categories" in interno and "totalTokens" in interno:
                return _trocar(_com_resposta(obj, _higienizar_contexto(interno)), troca)
        if str(resp.get("request_id", "")).startswith("init"):
            return None
    if tipo == "system" and obj.get("subtype") == "commands_changed":
        novo = dict(obj)
        novo["commands"] = _comandos_publicos(obj.get("commands"))
        return _trocar(novo, troca)
    if tipo == "system" and obj.get("subtype") == "init":
        novo = {k: obj[k] for k in INIT_CAMPOS if k in obj}
        if isinstance(novo.get("tools"), list):
            novo["tools"] = [t for t in novo["tools"] if not str(t).startswith("mcp__")]
        return _trocar(novo, troca)
    return _trocar(obj, troca)


_CAMPO_DELTA = {"text_delta": "text", "thinking_delta": "thinking", "input_json_delta": "partial_json"}


def _agrupar_deltas(objs):
    """Index the streamed deltas by block: {(parent, message_seq, index, field): [line positions]}."""
    grupos = {}
    seq = {}
    for i, obj in enumerate(objs):
        if not isinstance(obj, dict) or obj.get("type") != "stream_event":
            continue
        ev = obj.get("event")
        if not isinstance(ev, dict):
            continue
        pai = obj.get("parent_tool_use_id")
        if ev.get("type") == "message_start":
            seq[pai] = seq.get(pai, 0) + 1
        elif ev.get("type") == "content_block_delta" and isinstance(ev.get("delta"), dict):
            campo = _CAMPO_DELTA.get(ev["delta"].get("type"))
            if campo and isinstance(ev["delta"].get(campo), str):
                grupos.setdefault((pai, seq.get(pai, 0), ev.get("index"), campo), []).append(i)
    return grupos


def higienizar_stream_linhas(objs, troca):
    """Sanitize a whole stream, block-aware.

    A path can arrive split across several deltas of one block ("C:\\\\Us", "ers\\\\ca", ...), so the
    replacement runs on the joined text of each block. When that changes the result, the new
    text is spread over the same deltas (same count, proportional cuts), so the re-assembled
    block is valid and equals the sanitized final message.
    """
    limpos = [higienizar_stream(o, troca) for o in objs]
    for (_pai, _seq, _indice, campo), posicoes in _agrupar_deltas(objs).items():
        originais = [objs[i]["event"]["delta"][campo] for i in posicoes]
        novo = _trocar_texto("".join(originais), troca)
        if "".join(limpos[i]["event"]["delta"][campo] for i in posicoes) == novo:
            continue
        total = sum(len(t) for t in originais) or 1
        acumulado, inicio = 0, 0
        for n, i in enumerate(posicoes):
            acumulado += len(originais[n])
            fim = len(novo) if n == len(posicoes) - 1 else round(acumulado * len(novo) / total)
            fim = max(fim, inicio)
            limpos[i]["event"]["delta"][campo] = novo[inicio:fim]
            inicio = fim
    return [o for o in limpos if o is not None]


def higienizar_transcript(obj, troca):
    if not isinstance(obj, dict) or obj.get("type") not in TRANSCRIPT_TIPOS:
        return None
    novo = {k: obj[k] for k in TRANSCRIPT_CAMPOS if k in obj}
    return _trocar(novo, troca)


# ---------------------------------------------------------------- recording

def _bloco_png(tipo, corpo):
    return struct.pack(">I", len(corpo)) + tipo + corpo + struct.pack(">I", zlib.crc32(tipo + corpo))


def gerar_png(largura, altura):
    """A PNG of two solid colour bands (red on top, blue below), RGB 8 bits, made with the stdlib."""
    meio = altura // 2
    linhas = b"".join(bytes([0]) + (bytes((220, 40, 40)) if y < meio else bytes((40, 80, 220))) * largura
                      for y in range(altura))
    assinatura = bytes([137]) + b"PNG" + bytes([13, 10, 26, 10])
    return (assinatura + _bloco_png(b"IHDR", struct.pack(">IIBBBBB", largura, altura, 8, 2, 0, 0, 0)) +
            _bloco_png(b"IDAT", zlib.compress(linhas)) + _bloco_png(b"IEND", b""))


class Gravacao:
    def __init__(self, cenario, pasta):
        self.cenario = cenario
        self.pasta = pasta
        self.eventos = []          # parsed stdout objects, in order
        self.fila = queue.Queue()
        self.resultados = 0
        self.session_id = None
        self.lock = threading.Lock()
        self.pedidos_respondidos = 0
        self.respostas = list(cenario.get("respostas", []))
        self.proc = None
        self.erros_resposta = []

    def enviar(self, obj):
        dados = (json.dumps(obj, ensure_ascii=False) + "\n").encode("utf-8")
        self.proc.stdin.write(dados)
        self.proc.stdin.flush()

    def _ler_stdout(self):
        for bruto in self.proc.stdout:
            linha = bruto.decode("utf-8", errors="replace").rstrip("\r\n")
            try:
                obj = json.loads(linha)
            except ValueError:
                log("linha nao JSON ignorada (%d bytes)" % len(linha))
                continue
            if not isinstance(obj, dict):
                continue
            with self.lock:
                self.eventos.append(obj)
                if obj.get("type") == "system" and obj.get("subtype") == "init":
                    self.session_id = obj.get("session_id")
                if obj.get("type") == "result":
                    self.resultados += 1
            if obj.get("type") == "control_request":
                try:
                    self._responder_pedido(obj)
                except OSError:
                    pass
            self.fila.put(obj)
        self.fila.put(None)

    def _ler_stderr(self):
        for bruto in self.proc.stderr:
            texto = bruto.decode("utf-8", errors="replace").rstrip()
            if texto:
                log("claude stderr: " + texto[:200])

    def _responder_pedido(self, obj):
        req = obj.get("request") or {}
        if req.get("subtype") != "can_use_tool":
            return
        if self.cenario.get("sem_resposta"):
            log("pedido %s deixado sem resposta" % req.get("tool_name"))
            return
        n = self.pedidos_respondidos
        self.pedidos_respondidos += 1
        if n < len(self.respostas):
            resposta = self.respostas[n]
        elif self.respostas and self.cenario.get("repetir_ultima"):
            resposta = self.respostas[-1]
        else:
            resposta = "allow"
        entrada = req.get("input", {})
        if resposta == "allow":
            corpo = {"behavior": "allow", "updatedInput": entrada}
        elif resposta == "allow_always":
            corpo = {"behavior": "allow", "updatedInput": entrada,
                     "updatedPermissions": req.get("permission_suggestions", [])}
        elif resposta.startswith("deny:"):
            corpo = {"behavior": "deny", "message": resposta[5:]}
        else:
            raise ValueError("resposta desconhecida: " + resposta)
        log("pedido %s -> %s" % (req.get("tool_name"), resposta.split(":")[0]))
        self.enviar({"type": "control_response", "response": {
            "subtype": "success", "request_id": obj.get("request_id"), "response": corpo}})

    def _esperar(self, predicado, prazo=PRAZO_S):
        fim = time.time() + prazo
        while True:
            with self.lock:
                for ev in self.eventos:
                    if predicado(ev):
                        return ev
            if time.time() >= fim:
                return None
            try:
                if self.fila.get(timeout=0.5) is None:
                    self.fila.put(None)
                    time.sleep(0.2)
            except queue.Empty:
                pass

    def _esperar_resultado(self, alvo):
        fim = time.time() + PRAZO_S
        while time.time() < fim:
            with self.lock:
                if self.resultados >= alvo:
                    return True
            if self.proc.poll() is not None:
                time.sleep(0.5)
                with self.lock:
                    return self.resultados >= alvo
            time.sleep(0.2)
        return False

    def _controle(self, request_id, request):
        self.enviar({"type": "control_request", "request_id": request_id, "request": request})
        ev = self._esperar(lambda e: e.get("type") == "control_response" and
                           isinstance(e.get("response"), dict) and
                           e["response"].get("request_id") == request_id, 30)
        if ev is None:
            log("sem control_response para " + request_id)
            return None
        resp = ev["response"]
        if resp.get("subtype") != "success":
            self.erros_resposta.append((request_id, resp))
            log("control_response %s: %s" % (request_id, json.dumps(resp)[:300]))
        else:
            log("control_response %s: success" % request_id)
        return ev

    def _mensagem(self, conteudo):
        self.enviar({"type": "user", "message": {"role": "user", "content": conteudo},
                     "parent_tool_use_id": None, "session_id": "default"})

    def executar(self, comando):
        self.proc = subprocess.Popen(comando, cwd=self.pasta, stdin=subprocess.PIPE,
                                     stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        threading.Thread(target=self._ler_stdout, daemon=True).start()
        threading.Thread(target=self._ler_stderr, daemon=True).start()
        passos = self.cenario["passos"]
        if not any(p.get("inicializar") for p in passos):
            self.enviar({"type": "control_request", "request_id": "init-1",
                         "request": {"subtype": "initialize"}})
        enviadas = 0
        for i, passo in enumerate(passos):
            if "enviar" in passo:
                self._mensagem(passo["enviar"])
                enviadas += 1
                proximo = passos[i + 1] if i + 1 < len(passos) else {}
                if "interromper_apos_s" in proximo:
                    continue
                if not self._esperar_resultado(enviadas):
                    log("prazo estourado esperando o result")
                    break
            elif "interromper_apos_s" in passo:
                if self.cenario.get("sem_resposta"):
                    # count the seconds from the unanswered permission request, so the
                    # interruption always cancels a pending request (startup time varies)
                    self._esperar(lambda e: e.get("type") == "control_request" and
                                  (e.get("request") or {}).get("subtype") == "can_use_tool", 60)
                time.sleep(passo["interromper_apos_s"])
                self.enviar({"type": "control_request", "request_id": "interrupt-%d" % i,
                             "request": {"subtype": "interrupt"}})
                if not self._esperar_resultado(enviadas):
                    log("prazo estourado esperando o result da interrupcao")
                    break
            elif "inicializar" in passo:
                self._controle("init-1", {"subtype": "initialize"})
            elif "imagem" in passo or "comando" in passo:
                if "imagem" in passo:
                    img = passo["imagem"]
                    dados = base64.b64encode(gerar_png(img["largura"], img["altura"])).decode("ascii")
                    self._mensagem([{"type": "image", "source": {"type": "base64", "media_type": "image/png",
                                                                 "data": dados}},
                                    {"type": "text", "text": img["texto"]}])
                else:
                    self._mensagem(passo["comando"])
                enviadas += 1
                if not self._esperar_resultado(enviadas):
                    log("prazo estourado esperando o result")
                    break
            elif "esforco" in passo:
                self._controle("effort-%d" % i, {"subtype": "apply_flag_settings",
                                                 "settings": {"effortLevel": passo["esforco"]}})
                self._controle("settings-%d" % i, {"subtype": "get_settings"})
            elif "contexto" in passo:
                self._controle("context-%d" % i, {"subtype": "get_context_usage"})
            elif "modo" in passo:
                self._controle("mode-%d" % i, {"subtype": "set_permission_mode", "mode": passo["modo"]})
            elif "modelo" in passo:
                self._controle("model-%d" % i, {"subtype": "set_model", "model": passo["modelo"]})
        try:
            self.proc.stdin.close()
        except OSError:
            pass
        try:
            self.proc.wait(timeout=30)
        except subprocess.TimeoutExpired:
            self.proc.terminate()
            self.proc.wait(timeout=10)
        time.sleep(0.5)


# ---------------------------------------------------------------- expectations

def _blocos(ev):
    msg = ev.get("message")
    cont = msg.get("content") if isinstance(msg, dict) else None
    return [b for b in cont if isinstance(b, dict)] if isinstance(cont, list) else []


def _nomes_por_id(eventos):
    nomes = {}
    for ev in eventos:
        if ev.get("type") == "assistant":
            for b in _blocos(ev):
                if b.get("type") == "tool_use":
                    nomes[b.get("id")] = b.get("name")
    return nomes


def _tool_results(eventos):
    for ev in eventos:
        if ev.get("type") == "user":
            for b in _blocos(ev):
                if b.get("type") == "tool_result":
                    yield ev, b


def _texto(conteudo):
    if isinstance(conteudo, str):
        return conteudo
    if isinstance(conteudo, list):
        return "\n".join(c.get("text", "") for c in conteudo if isinstance(c, dict))
    return ""


def _recusa(ev):
    """A denied tool call comes back as an error tool_result; the deny message is its text.

    Without a message, tool_use_result is the string "User rejected tool use"; with a
    message it is "Error: <message>".
    """
    resultado = ev.get("tool_use_result")
    if isinstance(resultado, str) and (resultado == "User rejected tool use" or resultado.startswith("Error:")):
        return any(b.get("type") == "tool_result" and b.get("is_error") for b in _blocos(ev))
    return False


def conferir(espera, eventos):
    """Return True when the expectation holds."""
    nomes = _nomes_por_id(eventos)
    nome, _, arg = espera.partition(":")
    if nome == "pedido":
        return any(e.get("type") == "control_request" and
                   (e.get("request") or {}).get("tool_name") == arg for e in eventos)
    if nome == "resultado_ok":
        return any(nomes.get(b.get("tool_use_id")) == arg and not b.get("is_error")
                   for _, b in _tool_results(eventos))
    if nome == "bash_30_linhas":
        return any(nomes.get(b.get("tool_use_id")) in ("Bash", "PowerShell") and
                   len(_texto(b.get("content")).splitlines()) >= 30
                   for _, b in _tool_results(eventos))
    if nome == "subagente":
        return any(e.get("parent_tool_use_id") for e in eventos)
    if nome == "tool_result_erro":
        return any(b.get("is_error") is True for _, b in _tool_results(eventos))
    if nome == "recusado_e_resposta":
        for i, ev in enumerate(eventos):
            if ev.get("type") == "user" and _recusa(ev):
                return any(e.get("type") == "assistant" and
                           any(b.get("type") == "text" and b.get("text", "").strip() for b in _blocos(e))
                           for e in eventos[i + 1:])
        return False
    if nome == "interrompido":
        return any(e.get("type") == "control_cancel_request" or
                   (e.get("type") == "result" and e.get("subtype") == "error_during_execution")
                   for e in eventos)
    if nome == "set_model_ok":
        return any(e.get("type") == "control_response" and isinstance(e.get("response"), dict) and
                   str(e["response"].get("request_id", "")).startswith("model") and
                   e["response"].get("subtype") == "success" for e in eventos)
    if nome == "result_success":
        resultados = [e for e in eventos if e.get("type") == "result"]
        return bool(resultados) and resultados[-1].get("subtype") == "success"
    raise ValueError("espera desconhecida: " + espera)


# ---------------------------------------------------------------- main

def localizar_transcript(session_id):
    base = os.path.join(os.path.expanduser("~"), ".claude", "projects", "*", session_id + ".jsonl")
    achados = glob.glob(base)
    return achados[0] if achados else None


def gravar_cenario(cenario, saida):
    cid = cenario["id"]
    carimbo = datetime.now().strftime("%Y%m%d-%H%M%S")
    temp = os.environ.get("TEMP") or os.path.expanduser("~")
    pasta = os.path.join(temp, "cvc-demo", "%s-%s" % (cid, carimbo))
    os.makedirs(pasta)
    for nome, conteudo in (cenario.get("setup") or {}).items():
        destino = os.path.join(pasta, nome)
        os.makedirs(os.path.dirname(destino), exist_ok=True)
        with open(destino, "w", encoding="utf-8", newline="\n") as f:
            f.write(conteudo)
    claude = shutil.which("claude")
    if not claude:
        log("claude nao encontrado no PATH")
        return False
    comando = [claude, "-p", "--input-format", "stream-json", "--output-format", "stream-json",
               "--verbose", "--include-partial-messages", "--permission-prompt-tool", "stdio",
               "--model", "haiku", "--setting-sources", "project", "--strict-mcp-config"]
    log("[%s] gravando em %s" % (cid, pasta))
    g = Gravacao(cenario, pasta)
    g.executar(comando)
    eventos = list(g.eventos)
    custo = sum(e.get("total_cost_usd", 0) or 0 for e in eventos if e.get("type") == "result")
    log("[%s] custo total_cost_usd=%.4f" % (cid, custo))
    falhas = [e for e in cenario.get("espera", []) if not conferir(e, eventos)]
    if falhas:
        log("[%s] esperas nao atendidas: %s" % (cid, ", ".join(falhas)))
        return False
    home = os.path.expanduser("~")
    usuario = os.environ.get("USERNAME") or os.path.basename(home)
    troca = trocas_da_gravacao(pasta, home, usuario)
    hist = localizar_transcript(g.session_id) if g.session_id else None
    if not hist:
        log("[%s] historico da sessao nao encontrado" % cid)
        return False
    os.makedirs(saida, exist_ok=True)
    with open(os.path.join(saida, cid + ".stream.ndjson"), "w", encoding="utf-8", newline="\n") as f:
        for limpo in higienizar_stream_linhas(eventos, troca):
            f.write(json.dumps(limpo, ensure_ascii=False) + "\n")
    _escrever_historico(hist, os.path.join(saida, cid + ".transcript.jsonl"), troca)
    for rid, _resp in g.erros_resposta:
        log("[%s] aviso: %s respondeu com erro" % (cid, rid))
    log("[%s] ok" % cid)
    return True


def _escrever_historico(hist, destino, troca):
    with open(hist, encoding="utf-8") as src, open(destino, "w", encoding="utf-8", newline="\n") as dst:
        for linha in src:
            try:
                obj = json.loads(linha)
            except ValueError:
                continue
            limpo = higienizar_transcript(obj, troca)
            if limpo is not None:
                dst.write(json.dumps(limpo, ensure_ascii=False) + "\n")


def _session_id(stream):
    for obj in _ler_ndjson(stream):
        if isinstance(obj, dict) and obj.get("type") == "system" and obj.get("subtype") == "init" \
                and obj.get("session_id"):
            return obj["session_id"]
    return None


def re_extrair_historico(saida, projetos=None, temp=None, home=None, usuario=None):
    """Extract the history of each recording in `saida` again from its original Claude Code session.

    The session id comes from the `init` of `<scenario>.stream.ndjson`. Only the session file
    inside the recording's own project folder (`...cvc-demo-<scenario>-<stamp>/<id>.jsonl`) is read;
    the stamp in that folder name rebuilds the recording folder, so the replacements are the
    same as when it was recorded. Returns False when a session is missing.
    """
    projetos = projetos or os.path.join(os.path.expanduser("~"), ".claude", "projects")
    temp = temp or os.environ.get("TEMP") or os.path.expanduser("~")
    home = home or os.path.expanduser("~")
    usuario = usuario if usuario is not None else (os.environ.get("USERNAME") or os.path.basename(home))
    ok = True
    for stream in sorted(glob.glob(os.path.join(saida, "*.stream.ndjson"))):
        cid = os.path.basename(stream)[:-len(".stream.ndjson")]
        sid = _session_id(stream)
        padrao = re.compile(r"cvc-demo-" + re.escape(cid) + "-" + _CARIMBO + "$")
        achados = []
        for pasta_sessao in glob.glob(os.path.join(projetos, "*cvc-demo-" + glob.escape(cid) + "-*")):
            m = padrao.search(os.path.basename(pasta_sessao))
            arquivo = os.path.join(pasta_sessao, "%s.jsonl" % sid)
            if sid and m and os.path.isfile(arquivo):
                achados.append((m.group(1), arquivo))
        if len(achados) != 1:
            log("[%s] sessao original %s: %d achadas" % (cid, sid, len(achados)))
            ok = False
            continue
        carimbo, hist = achados[0]
        pasta = os.path.join(temp, "cvc-demo", "%s-%s" % (cid, carimbo))
        _escrever_historico(hist, os.path.join(saida, cid + ".transcript.jsonl"),
                            trocas_da_gravacao(pasta, home, usuario))
        log("[%s] historico re-extraido (carimbo %s)" % (cid, carimbo))
    return ok


def trocas_da_gravacao(pasta, home, usuario):
    """Replacements for a recording made in `pasta` (with and without resolving the folder)."""
    troca = montar_trocas(os.path.realpath(pasta), home, usuario)
    troca += [p for p in montar_trocas(pasta, home, "") if p not in troca]
    troca.sort(key=lambda p: len(p[0]), reverse=True)
    return troca


def _ler_ndjson(caminho):
    objs = []
    with open(caminho, encoding="utf-8") as f:
        for linha in f:
            if linha.strip():
                try:
                    objs.append(json.loads(linha))
                except ValueError:
                    pass
    return objs


def _escrever_ndjson(caminho, objs):
    with open(caminho, "w", encoding="utf-8", newline="\n") as f:
        for o in objs:
            f.write(json.dumps(o, ensure_ascii=False) + "\n")


_CARIMBO = r"(\d{8}-\d{6})"


def _carimbos(cid, textos):
    """Find the recording stamps of scenario `cid` in texts that may spell the folder as a path or as a slug."""
    padrao = re.compile(r"cvc-demo(?:\\\\|\\|/|-)+" + re.escape(cid) + "-" + _CARIMBO, re.IGNORECASE)
    return {m.group(1) for t in textos for m in padrao.finditer(t)}


def _trocas_re(cid, carimbos):
    """Replacements for files whose user name is already `demo`: the recording folder becomes C:\\demo\\app."""
    base_demo = DEMO_HOME + r"\AppData\Local\Temp\cvc-demo"
    troca = []
    for carimbo in sorted(carimbos):
        troca += montar_trocas("%s\\%s-%s" % (base_demo, cid, carimbo), DEMO_HOME, "")
    return sorted(set(troca), key=lambda p: len(p[0]), reverse=True)


def re_higienizar(pasta):
    """Apply the current replacements again to recordings already in `pasta`.

    The files already have the user name as `demo`, so the recording folder is
    `C:\\Users\\demo\\AppData\\Local\\Temp\\cvc-demo\\<scenario>-<stamp>`. The stamp is found
    in the re-assembled delta blocks (where the path may still be split) and in the history.
    """
    streams, historicos, carimbos = {}, {}, {}
    for caminho in sorted(glob.glob(os.path.join(pasta, "*.stream.ndjson"))):
        cid = os.path.basename(caminho)[:-len(".stream.ndjson")]
        objs = _ler_ndjson(caminho)
        streams[cid] = (caminho, objs)
        textos = ["".join(objs[i]["event"]["delta"][campo] for i in pos)
                  for (_p, _s, _x, campo), pos in _agrupar_deltas(objs).items()]
        textos.append(json.dumps(objs, ensure_ascii=False))
        carimbos.setdefault(cid, set()).update(_carimbos(cid, textos))
    for caminho in sorted(glob.glob(os.path.join(pasta, "*.transcript.jsonl"))):
        cid = os.path.basename(caminho)[:-len(".transcript.jsonl")]
        objs = _ler_ndjson(caminho)
        historicos[cid] = (caminho, objs)
        carimbos.setdefault(cid, set()).update(_carimbos(cid, [json.dumps(objs, ensure_ascii=False)]))
    for cid, (caminho, objs) in streams.items():
        troca = _trocas_re(cid, carimbos[cid])
        _escrever_ndjson(caminho, higienizar_stream_linhas(objs, troca))
        log("[%s] re-higienizado (carimbos: %s)" % (cid, ", ".join(sorted(carimbos[cid])) or "nenhum"))
    for cid, (caminho, objs) in historicos.items():
        troca = _trocas_re(cid, carimbos[cid])
        _escrever_ndjson(caminho, [h for h in (higienizar_transcript(o, troca) for o in objs) if h is not None])
        log("[%s] historico re-higienizado" % cid)


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("cenario", nargs="*")
    ap.add_argument("--todos", action="store_true")
    ap.add_argument("--re-higienizar", action="store_true",
                    help="apply the current replacements to the recordings already in --saida")
    ap.add_argument("--re-extrair-historico", action="store_true",
                    help="extract the histories in --saida again from the original Claude Code sessions")
    ap.add_argument("--saida", default=os.path.join(RAIZ, "tests", "fixtures"))
    args = ap.parse_args(argv)
    if args.re_higienizar:
        re_higienizar(args.saida)
        return 0
    if args.re_extrair_historico:
        return 0 if re_extrair_historico(args.saida) else 1
    with open(CENARIOS, encoding="utf-8") as f:
        todos = json.load(f)
    if args.todos:
        escolhidos = todos
    elif args.cenario:
        escolhidos = [c for c in todos if c["id"] in args.cenario]
        desconhecidos = [n for n in args.cenario if n not in {c["id"] for c in todos}]
        if desconhecidos:
            log("cenario desconhecido: " + ", ".join(desconhecidos))
            return 2
    else:
        ap.error("informe um cenario ou --todos")
    falhou = False
    for c in escolhidos:
        if not gravar_cenario(c, args.saida):
            falhou = True
    return 1 if falhou else 0


if __name__ == "__main__":
    sys.exit(main())
