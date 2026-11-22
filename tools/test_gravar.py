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
            "gridRows": [[1, 2]], "mcp_tools": [{"name": "mcp__x"}], "memoryFiles": [HOME + r"\a.md"],
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


def test_result_drops_when_the_login_expires():
    resultado = {"type": "result", "subtype": "success", "session_id": "s", "total_cost_usd": 0.01,
                 "credential_expires_at": "2026-11-28T22:27:46.138Z"}
    out = gravar.higienizar_stream(resultado, trocas())
    assert "credential_expires_at" not in out
    assert out == {"type": "result", "subtype": "success", "session_id": "s", "total_cost_usd": 0.01}
    assert "credential_expires_at" in resultado  # the line read is left as it was


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


def test_transcript_keeps_only_a_queued_command_attachment():
    linha = {
        "type": "attachment", "uuid": "a1", "parentUuid": "p1", "isSidechain": False,
        "timestamp": "2026-10-19T10:00:00Z", "sessionId": "s1",
        "cwd": PASTA, "gitBranch": "main", "version": "2.1.323",
        "attachment": {
            "type": "queued_command", "prompt": "open " + PASTA, "commandMode": "prompt",
            "origin": {"kind": "human"}, "source_uuid": "s0", "isMeta": False,
            "delivery_id": "d1", "usage": {"input_tokens": 1}, "cwd": PASTA,
        },
    }
    out = gravar.higienizar_transcript(linha, trocas())
    assert set(out) == {"type", "uuid", "parentUuid", "isSidechain", "timestamp", "sessionId", "attachment"}
    assert set(out["attachment"]) == {"type", "prompt", "commandMode", "origin", "source_uuid", "isMeta"}
    assert out["attachment"]["prompt"] == r"open C:\demo\app"
    blocos = dict(linha, attachment=dict(linha["attachment"], prompt=[
        {"type": "text", "text": "in " + PASTA}, {"type": "image", "source": {"data": "x"}}]))
    out = gravar.higienizar_transcript(blocos, trocas())
    assert out["attachment"]["prompt"][0]["text"] == r"in C:\demo\app"
    assert out["attachment"]["prompt"][1]["type"] == "image"
    outro = dict(linha, attachment={"type": "todo_reminder", "content": []})
    assert gravar.higienizar_transcript(outro, trocas()) is None
    assert gravar.higienizar_transcript({"type": "queue-operation", "operation": "enqueue", "content": "x"}, trocas()) is None


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


HASH = "0123456789abcdef"


def test_claude_project_hash_folder_is_replaced_in_every_spelling():
    # Claude Code 2.1.330 names the project's temp folder with a hash of the real working folder.
    obj = {"one": r"C:\Users\alice\AppData\Local\Temp\claude\%s\s\tasks\t.output" % HASH,
           "two": "C:\\\\Users\\\\alice\\\\AppData\\\\Local\\\\Temp\\\\claude\\\\%s\\\\s" % HASH,
           "slash": "C:/Users/alice/AppData/Local/Temp/claude/%s/s" % HASH.upper(),
           "end": r"written to C:\Users\alice\AppData\Local\Temp\claude\%s" % HASH,
           "other": r"C:\x\claude\%s\s and C:\Temp\claude\%s0\s" % (HASH, HASH)}
    out = gravar.higienizar_stream(obj, trocas())
    assert out["one"] == r"C:\Users\demo\AppData\Local\Temp\claude\C--demo-app\s\tasks\t.output"
    assert out["two"] == "C:\\\\Users\\\\demo\\\\AppData\\\\Local\\\\Temp\\\\claude\\\\C--demo-app\\\\s"
    assert out["slash"] == "C:/Users/demo/AppData/Local/Temp/claude/C--demo-app/s"
    assert out["end"] == r"written to C:\Users\demo\AppData\Local\Temp\claude\C--demo-app"
    assert out["other"] == obj["other"]  # not the Temp\claude folder, or not a 16-hex name
    hist = gravar.higienizar_transcript(
        {"type": "user", "message": {"content": obj["one"]}}, trocas())
    assert hist["message"]["content"] == out["one"]


def test_claude_project_hash_folder_split_across_deltas_is_replaced():
    texto = json.dumps({"file": r"C:\Users\alice\AppData\Local\Temp\claude\%s\s\t.output" % HASH})
    partes = _cortar(texto, "Temp", "claude", HASH[:5], HASH[9:])
    objs = [{"type": "stream_event", "parent_tool_use_id": None,
             "event": {"type": "message_start", "message": {"id": "m1"}}}]
    objs += [_delta(None, 0, "partial_json", "input_json_delta", p) for p in partes]
    out = gravar.higienizar_stream_linhas(objs, trocas())
    juntos = "".join(o["event"]["delta"]["partial_json"] for o in out if "delta" in o["event"])
    assert json.loads(juntos) == {"file": r"C:\Users\demo\AppData\Local\Temp\claude\C--demo-app\s\t.output"}


def test_re_higienizar_replaces_the_claude_project_hash_folder():
    import tempfile
    caminho_hash = r"C:\Users\demo\AppData\Local\Temp\claude\%s\s\tasks\t.output" % HASH
    limpo = r"C:\Users\demo\AppData\Local\Temp\claude\C--demo-app\s\tasks\t.output"
    with tempfile.TemporaryDirectory() as pasta:
        stream = os.path.join(pasta, "stop-task.stream.ndjson")
        hist = os.path.join(pasta, "stop-task.transcript.jsonl")
        with open(stream, "w", encoding="utf-8", newline="\n") as f:
            f.write(json.dumps({"type": "system", "subtype": "task_notification",
                                "output_file": caminho_hash}) + "\n")
        with open(hist, "w", encoding="utf-8", newline="\n") as f:
            f.write(json.dumps({"type": "user", "message": {"content": "see " + caminho_hash}}) + "\n")
        gravar.re_higienizar(pasta)
        with open(stream, encoding="utf-8") as f:
            linha_stream = json.loads(f.read())
        with open(hist, encoding="utf-8") as f:
            linha_hist = json.loads(f.read())
    assert linha_stream["output_file"] == limpo
    assert linha_hist["message"]["content"] == "see " + limpo


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


def _pedido(**extra):
    req = {"subtype": "can_use_tool", "tool_name": "Write", "input": {}}
    req.update(extra)
    return {"type": "control_request", "request_id": "r", "request": req}


def test_motivo_passa_com_o_tipo_e_o_texto():
    ev = [_pedido(decision_reason_type="hook", decision_reason="Asked by the hook.")]
    assert gravar.conferir("motivo:hook", ev)
    assert not gravar.conferir("motivo:safetyCheck", ev)


def test_motivo_falha_sem_texto():
    assert not gravar.conferir("motivo:hook", [_pedido(decision_reason_type="hook")])
    assert not gravar.conferir("motivo:hook", [_pedido(decision_reason_type="hook", decision_reason="")])


def test_pedido_sem_sugestao():
    assert gravar.conferir("pedido_sem_sugestao", [_pedido()])
    assert gravar.conferir("pedido_sem_sugestao", [_pedido(permission_suggestions=[])])
    assert not gravar.conferir("pedido_sem_sugestao", [_pedido(permission_suggestions=[{"type": "addRules"}])])


def _initialize(**interno):
    return {"type": "control_response", "response": {"subtype": "success", "request_id": "init-1", "response": interno}}


def test_initialize_keeps_the_auth_conflict_sources_only():
    conflito = {"token_source": "claude.ai", "api_key_source": "ANTHROPIC_API_KEY", "key": "sk-ant-fake"}
    limpo = gravar.higienizar_stream(_initialize(commands=[], auth_conflict=conflito, account={"email": "a@b.c"}), trocas())
    interno = limpo["response"]["response"]
    assert interno["auth_conflict"] == {"token_source": "claude.ai", "api_key_source": "ANTHROPIC_API_KEY"}
    assert "account" not in interno


def test_initialize_without_a_conflict_has_none():
    limpo = gravar.higienizar_stream(_initialize(commands=[]), trocas())
    assert "auth_conflict" not in limpo["response"]["response"]


def test_auth_conflict_check():
    com = _initialize(commands=[], auth_conflict={"token_source": "claude.ai", "api_key_source": "ANTHROPIC_API_KEY"})
    assert gravar.conferir("auth_conflict", [com])
    assert not gravar.conferir("auth_conflict", [_initialize(commands=[])])
    assert not gravar.conferir("auth_conflict", [_initialize(commands=[], auth_conflict={"token_source": "claude.ai"})])


def test_ambiente_adds_variables_to_the_child_environment_only():
    base = {"PATH": "x", "ANTHROPIC_API_KEY": "real"}
    env = gravar.ambiente_do_cenario({"ambiente": {"ANTHROPIC_API_KEY": "sk-ant-fake"}}, base)
    assert env["ANTHROPIC_API_KEY"] == "sk-ant-fake" and env["PATH"] == "x"
    assert base["ANTHROPIC_API_KEY"] == "real"
    assert gravar.ambiente_do_cenario({}, base) == base


def test_comando_uses_haiku_by_default():
    comando = gravar.comando_do_cenario("C:/x/claude.exe", {"id": "edit"})
    assert comando[0] == "C:/x/claude.exe"
    assert comando[comando.index("--model") + 1] == "haiku"
    assert comando[-1] == "--strict-mcp-config"  # no extra arguments
    for opcao in ("-p", "--input-format", "--output-format", "--verbose", "--include-partial-messages",
                  "--permission-prompt-tool", "--setting-sources", "--strict-mcp-config"):
        assert opcao in comando


def test_comando_takes_the_scenario_model_and_its_extra_arguments():
    comando = gravar.comando_do_cenario("claude", {"modelo_cli": "sonnet", "argumentos": ["--advisor", "opus"]})
    assert comando[comando.index("--model") + 1] == "sonnet"
    assert comando.count("--model") == 1
    assert comando[-2:] == ["--advisor", "opus"]
    assert comando[:-2] == gravar.comando_do_cenario("claude", {"modelo_cli": "sonnet"})


def test_encrypted_content_is_cut_in_the_stream_and_in_the_transcript():
    resultado = {"type": "advisor_tool_result", "tool_use_id": "srv1",
                 "content": {"type": "advisor_redacted_result", "encrypted_content": "QUJD" * 500}}
    linha = {"type": "assistant", "message": {"content": [
        {"type": "server_tool_use", "id": "srv1", "name": "advisor", "input": {}}, resultado,
        {"type": "text", "text": "see " + PASTA, "nested": [{"encrypted_content": {"deep": "x"}}]}]}}
    out = gravar.higienizar_stream(linha, trocas())
    blocos = out["message"]["content"]
    assert blocos[1]["content"] == {"type": "advisor_redacted_result", "encrypted_content": "redacted"}
    assert blocos[2]["nested"] == [{"encrypted_content": "redacted"}]
    assert blocos[2]["text"] == r"see C:\demo\app"
    assert resultado["content"]["encrypted_content"] == "QUJD" * 500  # the line read is left as it was
    evento = {"type": "stream_event", "event": {"type": "content_block_start", "index": 1,
                                                 "content_block": resultado}}
    out = gravar.higienizar_stream_linhas([evento], trocas())
    assert out[0]["event"]["content_block"]["content"]["encrypted_content"] == "redacted"
    hist = gravar.higienizar_transcript(dict(linha, uuid="u1", cwd=PASTA), trocas())
    assert hist["message"]["content"][1]["content"]["encrypted_content"] == "redacted"
    assert "QUJD" not in json.dumps(hist)


def _advisor_blocos(*blocos):
    return {"type": "assistant", "message": {"content": list(blocos)}}


def test_expectation_advisor():
    uso = {"type": "server_tool_use", "id": "srv1", "name": "advisor", "input": {}}
    resultado = {"type": "advisor_tool_result", "tool_use_id": "srv1", "content": {"type": "advisor_result"}}
    assert gravar.conferir("advisor", [_advisor_blocos(uso, resultado)])
    assert gravar.conferir("advisor", [_advisor_blocos(uso), _advisor_blocos(resultado)])
    assert not gravar.conferir("advisor", [_advisor_blocos(uso)])
    assert not gravar.conferir("advisor", [_advisor_blocos(resultado)])
    outro = dict(uso, name="web_search")
    assert not gravar.conferir("advisor", [_advisor_blocos(outro, resultado)])
    assert not gravar.conferir("advisor", [])


def _tarefas(*tarefas):
    return {"type": "system", "subtype": "background_tasks_changed", "tasks": list(tarefas)}


def test_expectation_background_task():
    wf = {"task_id": "w1", "task_type": "local_workflow", "description": "wf"}
    sh = {"task_id": "b1", "task_type": "local_bash", "description": "sh"}
    assert gravar.conferir("tarefa:local_workflow", [_tarefas(wf), _tarefas()])
    assert gravar.conferir("tarefa:local_workflow", [_tarefas(sh, wf), _tarefas(wf), _tarefas()])
    assert not gravar.conferir("tarefa:local_workflow", [_tarefas(wf)])  # never ended
    assert not gravar.conferir("tarefa:local_workflow", [_tarefas(), _tarefas(wf)])  # empty only before
    assert not gravar.conferir("tarefa:local_workflow", [_tarefas(sh), _tarefas()])
    assert gravar.conferir("tarefa:local_bash", [_tarefas(sh), _tarefas()])
    assert not gravar.conferir("tarefa:local_workflow", [])


def _resposta_controle(request_id, subtipo="success"):
    return {"type": "control_response", "response": {"subtype": subtipo, "request_id": request_id, "response": {}}}


def _notificacao(status):
    return {"type": "system", "subtype": "task_notification", "task_id": "b1", "status": status,
            "output_file": "x", "summary": "s"}


def test_expectation_stopped_task():
    assert gravar.conferir("tarefa_parada", [_resposta_controle("stop-1"), _notificacao("stopped")])
    assert not gravar.conferir("tarefa_parada", [_resposta_controle("stop-1")])
    assert not gravar.conferir("tarefa_parada", [_notificacao("stopped")])
    assert not gravar.conferir("tarefa_parada", [_resposta_controle("stop-1"), _notificacao("completed")])
    assert not gravar.conferir("tarefa_parada", [_resposta_controle("stop-1", "error"), _notificacao("stopped")])
    assert not gravar.conferir("tarefa_parada", [_resposta_controle("mode-1"), _notificacao("stopped")])


def test_stop_step_sends_stop_task_with_the_first_listed_task_id():
    eventos = [_tarefas(),
               _tarefas({"task_id": "b7", "task_type": "local_bash"}, {"task_id": "b8", "task_type": "local_bash"})]
    enviados, prazos = [], []
    g = gravar.Gravacao({"id": "stop-task", "passos": [{"parar_tarefa_apos_s": 0}]}, "C:/x")

    def esperar(predicado, prazo=gravar.PRAZO_S):
        prazos.append(prazo)
        return next((e for e in eventos if predicado(e)), None)

    def enviar(obj):
        enviados.append(obj)
        if obj.get("type") == "control_request":
            eventos.append(_resposta_controle(obj["request_id"]))

    g._esperar = esperar
    g.enviar = enviar
    g._parar_tarefa(3, {"parar_tarefa_apos_s": 0})
    assert enviados == [{"type": "control_request", "request_id": "stop-3",
                         "request": {"subtype": "stop_task", "task_id": "b7"}}]
    assert prazos[0] == 60
    assert g.erros_resposta == []


def test_stop_step_without_a_task_sends_nothing():
    enviados = []
    g = gravar.Gravacao({"id": "stop-task", "passos": []}, "C:/x")
    g._esperar = lambda predicado, prazo=gravar.PRAZO_S: None
    g.enviar = enviados.append
    g._parar_tarefa(0, {"parar_tarefa_apos_s": 0})
    assert enviados == []


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
