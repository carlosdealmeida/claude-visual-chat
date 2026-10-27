"""Tests for the sanitizer in gravar.py.

Run: python -I -m pytest tools/test_gravar.py   (or: python -I tools/test_gravar.py)
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import gravar  # noqa: E402

PASTA = r"C:\Users\alice\AppData\Local\Temp\cvc-demo\edit-20261019"
HOME = r"C:\Users\alice"
USUARIO = "alice"


def trocas():
    return gravar.montar_trocas(PASTA, HOME, USUARIO)


def test_init_keeps_only_allowed_fields():
    init = {
        "type": "system",
        "subtype": "init",
        "session_id": "abc",
        "model": "claude-haiku",
        "cwd": PASTA,
        "permissionMode": "default",
        "claude_code_version": "2.1.286",
        "tools": ["Read", "Edit", "mcp__claude_ai_Gmail__x", "Bash"],
        "mcp_servers": [{"name": "claude_ai_Gmail", "status": "connected"}],
        "skills": ["board"],
        "plugins": [{"name": "p"}],
        "memory_paths": {"auto": HOME + r"\.claude\memory"},
        "apiKeySource": "none",
    }
    out = gravar.higienizar_stream(init, trocas())
    assert set(out) == {"type", "subtype", "session_id", "model", "cwd",
                        "permissionMode", "claude_code_version", "tools"}
    assert out["tools"] == ["Read", "Edit", "Bash"]
    assert out["cwd"] == r"C:\demo\app"


def test_paths_are_replaced():
    obj = {
        "type": "assistant",
        "message": {"content": [
            {"type": "text", "text": "Folder " + PASTA + " of user Alice, home " + HOME},
            {"type": "tool_use", "id": "t1", "name": "Read",
             "input": {"file_path": PASTA + r"\notes.md",
                       "alt": PASTA.replace("\\", "/") + "/notes.md",
                       "esc": PASTA.replace("\\", "\\\\") + "\\\\x"}},
        ]},
    }
    out = gravar.higienizar_stream(obj, trocas())
    texto = json.dumps(out)
    assert "alice" not in texto.lower()
    assert "cvc-demo" not in texto
    blocos = out["message"]["content"]
    assert blocos[0]["text"] == r"Folder C:\demo\app of user demo, home C:\Users\demo"
    assert blocos[1]["input"]["file_path"] == r"C:\demo\app\notes.md"
    assert blocos[1]["input"]["alt"] == "C:/demo/app/notes.md"
    assert blocos[1]["input"]["esc"] == "C:\\\\demo\\\\app\\\\x"


def test_dropped_lines():
    assert gravar.higienizar_stream({"type": "rate_limit_event", "rate_limit_info": {}}, trocas()) is None
    outra = {"type": "control_response", "response": {
        "subtype": "success", "request_id": "mode-1", "response": {"mode": "acceptEdits"}}}
    assert gravar.higienizar_stream(outra, trocas()) == outra


COMANDOS_PUBLICOS = ["compact", "context", "usage", "cost", "model", "effort", "hello"]


def _comandos_reais():
    return [{"name": n, "description": "d " + n, "argumentHint": ""} for n in COMANDOS_PUBLICOS[:3]] + [
        {"name": "board", "description": "Cards do board do Carlos", "argumentHint": ""},
        {"name": "enel", "description": "Troca Enel da WA", "argumentHint": "cdc"},
    ] + [{"name": n, "description": "d " + n, "argumentHint": ""} for n in COMANDOS_PUBLICOS[3:]]


def test_higieniza_resposta_do_initialize():
    resp = {"type": "control_response", "response": {
        "subtype": "success", "request_id": "init-1",
        "response": {
            "commands": _comandos_reais(),
            "models": [{"value": "default", "resolvedModel": "claude-x", "displayName": "Default",
                        "description": "d", "supportsEffort": True,
                        "supportedEffortLevels": ["low", "high"]}],
            "agents": [{"name": "meu-agente", "description": "privado"}],
            "account": {"email": "a@b.com", "organization": "Org"},
            "available_output_styles": ["default", "meu-estilo"],
        }}}
    out = gravar.higienizar_stream(resp, trocas())
    interno = out["response"]["response"]
    assert [c["name"] for c in interno["commands"]] == COMANDOS_PUBLICOS
    assert interno["commands"][0]["description"] == "d compact"
    assert interno["models"][0]["value"] == "default"
    for campo in ("account", "available_output_styles", "agents"):
        assert campo not in interno
    texto = json.dumps(out)
    assert "board" not in texto and "enel" not in texto and "a@b.com" not in texto


def test_higieniza_get_settings():
    resp = {"type": "control_response", "response": {
        "subtype": "success", "request_id": "settings-2",
        "response": {
            "effective": {"env": {"TOKEN": "x"}, "permissions": {"allow": ["Bash(x)"]}, "model": "haiku"},
            "applied": {"model": "haiku", "effort": "high", "outro": "x"},
        }}}
    out = gravar.higienizar_stream(resp, trocas())
    assert out["response"]["response"] == {"applied": {"model": "haiku", "effort": "high"}}
    texto = json.dumps(out)
    for proibido in ("effective", "env", "permissions", "TOKEN"):
        assert proibido not in texto


def test_higieniza_context_usage():
    resp = {"type": "control_response", "response": {
        "subtype": "success", "request_id": "context-3",
        "response": {
            "categories": [{"name": "Messages", "tokens": 10, "color": "blue", "kind": "messages",
                            "isDeferred": False}],
            "totalTokens": 100, "maxTokens": 200000, "rawMaxTokens": 200000, "percentage": 0.5,
            "gridRows": [[1, 2]], "mcp_tools": [{"name": "mcp__x"}], "memoryFiles": [HOME + r".md"],
        }}}
    out = gravar.higienizar_stream(resp, trocas())
    assert out["response"]["response"] == {
        "categories": [{"name": "Messages", "tokens": 10, "kind": "messages"}],
        "totalTokens": 100, "maxTokens": 200000, "percentage": 0.5}


def test_higieniza_commands_changed():
    ev = {"type": "system", "subtype": "commands_changed", "commands": _comandos_reais(),
          "uuid": "u", "session_id": "s"}
    out = gravar.higienizar_stream(ev, trocas())
    assert [c["name"] for c in out["commands"]] == COMANDOS_PUBLICOS
    assert out["subtype"] == "commands_changed" and out["session_id"] == "s"


def test_gera_png_valido():
    import struct
    import zlib
    png = gravar.gerar_png(96, 48)
    assert png[:8] == bytes([137]) + b"PNG" + bytes([13, 10, 26, 10])
    pos, ihdr, dados = 8, None, b""
    while pos < len(png):
        n, tipo = struct.unpack(">I4s", png[pos:pos + 8])
        corpo = png[pos + 8:pos + 8 + n]
        assert zlib.crc32(tipo + corpo) == struct.unpack(">I", png[pos + 8 + n:pos + 12 + n])[0]
        if tipo == b"IHDR":
            ihdr = struct.unpack(">IIBBBBB", corpo)
        elif tipo == b"IDAT":
            dados += corpo
        pos += 12 + n
    assert ihdr[:2] == (96, 48)
    assert len(zlib.decompress(dados)) == 48 * (1 + 96 * 3)


def test_transcript_allowlist():
    linha = {
        "type": "assistant", "subtype": None, "uuid": "u1", "parentUuid": None,
        "isSidechain": False, "isMeta": False, "isCompactSummary": False,
        "timestamp": "2026-10-19T10:00:00Z", "sessionId": "s1",
        "message": {"role": "assistant", "content": [{"type": "text", "text": "in " + PASTA}]},
        "toolUseResult": {"filePath": PASTA + r"\a.txt"}, "toolDenialKind": "permission-rule",
        "thinkingDurationMs": 1234,
        "cwd": PASTA, "gitBranch": "main", "version": "2.1.286", "userType": "external",
        "requestId": "req", "entrypoint": "sdk-cli",
    }
    out = gravar.higienizar_transcript(linha, trocas())
    assert set(out) <= {"type", "subtype", "uuid", "parentUuid", "isSidechain", "isMeta",
                        "isCompactSummary", "timestamp", "sessionId", "message", "toolUseResult",
                        "toolDenialKind", "thinkingDurationMs"}
    assert out["toolDenialKind"] == "permission-rule"
    assert out["thinkingDurationMs"] == 1234  # how long a thought took: "Thought for 1s"
    assert "cwd" not in out and "gitBranch" not in out
    assert out["message"]["content"][0]["text"] == r"in C:\demo\app"
    assert out["toolUseResult"]["filePath"] == r"C:\demo\app\a.txt"
    for tipo in ("attachment", "mode", "api-request", "file-history-snapshot", "queue-operation"):
        assert gravar.higienizar_transcript({"type": tipo, "uuid": "x"}, trocas()) is None
    assert gravar.higienizar_transcript({"type": "system", "subtype": "compact_boundary"}, trocas()) is not None


def test_expectation_denied_needs_text_after_rejection():
    negado = {"type": "user", "tool_use_result": "Error: Do not create files, just explain.",
              "message": {"content": [{"type": "tool_result", "tool_use_id": "t", "is_error": True,
                                       "content": "Do not create files, just explain."}]}}
    texto = {"type": "assistant", "message": {"content": [{"type": "text", "text": "Done."}]}}
    assert gravar.conferir("recusado_e_resposta", [negado, texto])
    assert not gravar.conferir("recusado_e_resposta", [negado])
    assert not gravar.conferir("recusado_e_resposta", [texto, negado])


def _delta(parent, index, campo, tipo, valor):
    return {"type": "stream_event", "parent_tool_use_id": parent,
            "event": {"type": "content_block_delta", "index": index,
                      "delta": {"type": tipo, campo: valor}}}


def _cortar(texto, *trechos):
    """Cut `texto` in the middle of each given substring (so the words get split)."""
    pontos = sorted(texto.index(t) + len(t) // 2 for t in trechos)
    partes, ini = [], 0
    for p in pontos:
        partes.append(texto[ini:p])
        ini = p
    partes.append(texto[ini:])
    return partes


def test_input_json_delta_split_path_is_sanitized_per_block():
    json_texto = json.dumps({"file_path": PASTA + "\\notes.md", "n": 1})
    partes = _cortar(json_texto, "Users", "alice", "AppData", "cvc-demo", "edit-")
    outras = _cortar(json.dumps({"command": "ls " + HOME}), "alice")
    objs = [{"type": "stream_event", "parent_tool_use_id": None,
             "event": {"type": "message_start", "message": {"id": "m1"}}}]
    # two blocks interleaved at different indexes
    for i, p in enumerate(partes):
        objs.append(_delta(None, 1, "partial_json", "input_json_delta", p))
        if i < len(outras):
            objs.append(_delta(None, 2, "partial_json", "input_json_delta", outras[i]))
    for p in outras[len(partes):]:
        objs.append(_delta(None, 2, "partial_json", "input_json_delta", p))
    out = gravar.higienizar_stream_linhas(objs, trocas())
    assert len(out) == len(objs)  # same events, same chunk count
    por_indice = {}
    for o in out:
        d = o["event"].get("delta")
        if d:
            por_indice.setdefault(o["event"]["index"], []).append(d["partial_json"])
    assert json.loads("".join(por_indice[1])) == {"file_path": r"C:\demo\app\notes.md", "n": 1}
    assert json.loads("".join(por_indice[2])) == {"command": r"ls C:\Users\demo"}
    assert len(por_indice[1]) == len(partes)


def test_split_deltas_do_not_mix_between_messages_or_parents():
    ini = {"type": "stream_event", "parent_tool_use_id": None,
           "event": {"type": "message_start", "message": {"id": "m"}}}
    objs = [ini, _delta(None, 0, "text", "text_delta", "see C:\\Users\\ali"),
            _delta(None, 0, "text", "text_delta", "ce\\x"),
            ini, _delta(None, 0, "text", "text_delta", "ce here")]
    out = gravar.higienizar_stream_linhas(objs, trocas())
    textos = [o["event"]["delta"]["text"] for o in out if "delta" in o["event"]]
    assert "".join(textos[:2]) == "see C:\\Users\\demo\\x"
    assert textos[2] == "ce here"


def test_claude_folder_slug_is_replaced():
    slug_pasta = "C--Users-alice-AppData-Local-Temp-cvc-demo-edit-20261019"
    obj = {"outputFile": r"C:\Users\alice\AppData\Local\Temp\claude\%s\s\t.output" % slug_pasta,
           "other": "C--Users-alice-AppData-Roaming"}
    out = gravar.higienizar_stream(obj, trocas())
    assert out["outputFile"] == r"C:\Users\demo\AppData\Local\Temp\claude\C--demo-app\s\t.output"
    assert out["other"] == "C--Users-demo-AppData-Roaming"


def test_username_uses_word_boundary():
    out = gravar.higienizar_stream({"t": "alicesmith met Alice, alice_x and alice."}, trocas())
    assert out["t"] == "alicesmith met demo, alice_x and demo."


def test_re_higienizar_fixes_split_deltas_in_existing_files():
    import tempfile
    sujo = r"C:\Users\demo\AppData\Local\Temp\cvc-demo\edit-20261019-183128"
    texto = json.dumps({"file_path": sujo + "\\notes.md"})
    partes = _cortar(texto, "Users", "AppData", "cvc-demo", "edit-")
    objs = [{"type": "stream_event", "parent_tool_use_id": None,
             "event": {"type": "message_start", "message": {"id": "m1"}}}]
    objs += [_delta(None, 0, "partial_json", "input_json_delta", p) for p in partes]
    with tempfile.TemporaryDirectory() as pasta:
        caminho = os.path.join(pasta, "edit.stream.ndjson")
        with open(caminho, "w", encoding="utf-8", newline="\n") as f:
            for o in objs:
                f.write(json.dumps(o) + "\n")
        gravar.re_higienizar(pasta)
        with open(caminho, encoding="utf-8") as f:
            linhas = [json.loads(l) for l in f]
    juntos = "".join(o["event"]["delta"]["partial_json"] for o in linhas if "delta" in o["event"])
    assert json.loads(juntos) == {"file_path": r"C:\demo\app\notes.md"}


def test_re_extrair_historico_reads_only_the_recording_session():
    import tempfile
    sid = "11111111-2222-3333-4444-555555555555"
    carimbo = "20261019-183334"
    temp = HOME + r"\AppData\Local\Temp"
    pasta = temp + r"\cvc-demo\denied-" + carimbo
    sessao = [
        {"type": "user", "uuid": "u1", "sessionId": sid, "cwd": pasta, "gitBranch": "main",
         "message": {"role": "user", "content": "Write " + pasta + r"\secret.txt"}},
        {"type": "user", "uuid": "u2", "sessionId": sid, "toolDenialKind": "permission-rule",
         "toolUseResult": "Error: no",
         "message": {"role": "user", "content": [{"type": "tool_result", "tool_use_id": "t1",
                                                   "content": "no", "is_error": True}]}},
        {"type": "attachment", "uuid": "a1", "attachment": {"path": HOME + r"\.claude\CLAUDE.md"}},
    ]
    with tempfile.TemporaryDirectory() as raiz:
        saida = os.path.join(raiz, "fixtures")
        projetos = os.path.join(raiz, "projects")
        os.makedirs(saida)
        gravacao = os.path.join(projetos, gravar._slug(pasta))
        outra = os.path.join(projetos, "C--Users-alice-other")
        os.makedirs(gravacao)
        os.makedirs(outra)
        with open(os.path.join(saida, "denied.stream.ndjson"), "w", encoding="utf-8") as f:
            f.write(json.dumps({"type": "system", "subtype": "init", "session_id": sid,
                                "cwd": r"C:\demo\app"}) + "\n")
        with open(os.path.join(gravacao, sid + ".jsonl"), "w", encoding="utf-8") as f:
            for o in sessao:
                f.write(json.dumps(o) + "\n")
        with open(os.path.join(outra, sid + ".jsonl"), "w", encoding="utf-8") as f:
            f.write(json.dumps({"type": "user", "message": {"content": "from another folder"}}) + "\n")
        gravar.re_extrair_historico(saida, projetos=projetos, temp=temp, home=HOME, usuario=USUARIO)
        with open(os.path.join(saida, "denied.transcript.jsonl"), encoding="utf-8") as f:
            texto = f.read()
    linhas = [json.loads(l) for l in texto.splitlines()]
    assert [l["uuid"] for l in linhas] == ["u1", "u2"]
    assert linhas[0]["message"]["content"] == r"Write C:\demo\app\secret.txt"
    assert linhas[1]["toolDenialKind"] == "permission-rule"
    assert "alice" not in texto.lower() and "cvc-demo" not in texto and "another" not in texto


def test_localizar_claude_prefere_a_opcao():
    achado = gravar.localizar_claude("C:/x/claude.exe", which=lambda n: "C:/path/claude.exe",
                                     isfile=lambda p: p == "C:/x/claude.exe")
    assert achado == "C:/x/claude.exe"


def test_localizar_claude_cai_no_path():
    assert gravar.localizar_claude(None, which=lambda n: "C:/path/claude.exe",
                                   isfile=lambda p: True) == "C:/path/claude.exe"


def test_localizar_claude_recusa_opcao_que_nao_e_arquivo_mesmo_com_claude_no_path():
    try:
        gravar.localizar_claude("C:/nao/existe.exe", which=lambda n: "C:/path/claude.exe",
                                isfile=lambda p: False)
        assert False, "should have refused"
    except SystemExit as e:
        assert "C:/nao/existe.exe" in str(e)


def test_localizar_claude_recusa_quando_nao_ha_opcao_nem_claude_no_path():
    try:
        gravar.localizar_claude(None, which=lambda n: None, isfile=lambda p: False)
        assert False, "should have refused"
    except SystemExit as e:
        assert "claude nao encontrado" in str(e)


def test_resposta_modo_e_allow_manda_o_modo_antes():
    pedido = {"request_id": "r1", "request": {"input": {"plan": "x"}}}
    linhas = gravar.linhas_da_resposta("modo:acceptEdits+allow", pedido, lambda: "c9")
    assert len(linhas) == 2
    assert linhas[0]["type"] == "control_request"
    assert linhas[0]["request_id"] == "c9"
    assert linhas[0]["request"] == {"subtype": "set_permission_mode", "mode": "acceptEdits"}
    assert linhas[1]["response"]["response"] == {"behavior": "allow", "updatedInput": {"plan": "x"}}
    assert linhas[1]["response"]["request_id"] == "r1"


def test_resposta_deny_leva_a_mensagem():
    pedido = {"request_id": "r1", "request": {"input": {}}}
    linhas = gravar.linhas_da_resposta("deny:fica no plano", pedido, lambda: "c9")
    assert linhas == [{"type": "control_response", "response": {
        "subtype": "success", "request_id": "r1",
        "response": {"behavior": "deny", "message": "fica no plano"}}}]


def test_resposta_allow_always_leva_as_sugestoes():
    sug = [{"type": "addRules"}]
    pedido = {"request_id": "r1", "request": {"input": {"a": 1}, "permission_suggestions": sug}}
    linhas = gravar.linhas_da_resposta("allow_always", pedido, lambda: "c9")
    assert linhas[0]["response"]["response"] == {"behavior": "allow", "updatedInput": {"a": 1},
                                                 "updatedPermissions": sug}


if __name__ == "__main__":
    falhas = 0
    for nome, fn in sorted(globals().items()):
        if nome.startswith("test_") and callable(fn):
            try:
                fn()
                print("ok  ", nome)
            except Exception as e:  # noqa: BLE001
                falhas += 1
                print("FAIL", nome, repr(e))
    sys.exit(1 if falhas else 0)
