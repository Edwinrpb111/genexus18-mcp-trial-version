# MCP Friction Report — 2026-05-14

**MCP version:** 2.3.4 | **GeneXus:** 18.0.7.179127 | **KB:** AcademicoHomolog1

Sessão real: depuração + redesign de popup `RegProfAlunoUGPopup` em `ListaAtiCPAlunoUniGra`. ~50 chamadas MCP, 8 build cycles.

Só itens **acionáveis no código do MCP**. Quirks do GeneXus runtime / do IDE ficam fora (esses viram doc/CLAUDE.md ou skill).

---

## P0 — High-impact

### #1 — `var:N` em layout XML é opaco; nenhum tool expõe o mapping
**Sintoma:** Layout usa `<gxAttribute AttID="var:8" />`. Pra saber qual variável é `var:8`, tive que `grep -E 'AV[0-9]+Variable' regprofalunougpopup.cs` no .cs gerado. `genexus_read part="Layout"` e `genexus_inspect include=["variables"]` não expõem o ID interno. Em `RegProfAlunoUGPopup` a layout estava bindada em var IDs errados (var:4=Pgmdesc, var:5=Alu2AnoCad em vez de Alu2RegProf/Alu2NumRegProf) — bug invisível sem essa info.
**Sugestão:**
- `genexus_inspect include=["variables"]` retornar `internalId` por variável.
- `genexus_read part="Layout"` aceitar flag `resolveAttIds=true` que mostra `AttID="var:8"` como comentário `<!-- &Alu2RegProf -->`.
- `genexus_edit` aceitar referência simbólica (`Attribute="&Alu2RegProf"`) e resolver pro ID interno no save.

### #2 — `genexus_edit` retorna `Error` com persist real (falso-negativo)
**Sintoma:** Resposta `status: "Error", persistedVerified: false, fallbackWriteStatus: "NoChange"` em 2 chamadas — mas `genexus_read` em seguida confirma que o arquivo FOI persistido com a mudança. Faz o agente re-tentar inutilmente.
**Sugestão:** corrigir a verificação de persistência. No mínimo, retornar `status: "PartialSuccess"` quando há ambiguidade, com diff visível pro caller decidir.

### #3 — `genexus_analyze mode=impact` retorna `totalAffected=0` com callers reais
**Sintoma:** `RetAluUniGra` chamada literalmente por `ListaAtiCPAlunoUniGra` (`RetAluUniGra(&Alu2AnoCad, ...)` no Source). `analyze(mode=impact, name=RetAluUniGra)` retornou `totalAffected: 0, blastRadiusScore: 0, riskLevel: Low`. Quase deixei de buildar os callers — quebraria DLL.
**Sugestão:** revisar reverse-dep index. Call sites em Event Start aparentemente não indexados. Validar cross-check vs `genexus_search_source callee=<name>`.

### #4 — Variable Properties (ControlType, ControlValues) sem API
**Sintoma:** Pra setar `&Alu2RegProf` como Radio Button preciso editar Layout XML manualmente. `genexus_properties` só cobre object-level. Forçou hardcode no XML do `<gxAttribute ControlType="Radio Button" ControlValues="Sim:S,Não:N">` (que ainda funciona, mas a config "correta" é variable-level).
**Sugestão:** estender `genexus_properties`:
```
genexus_properties action=set name=<obj> variable=&Alu2RegProf
    propertyName=ControlType value="Radio Button"
```

### #5 — `genexus_properties` não cobre per-control / per-variable (relacionado a #4)
**Sintoma:** `genexus_properties control=vMODULO` → `Control 'vMODULO' not found`. Só funciona pra objeto inteiro. Mesmo com nomes corretos do .cs (`vMODULO`, `BtnConfirmar`) ou variável (`modulo`, `&modulo`), tool não encontra.
**Sugestão:** aceitar 3 modos no `control=`:
- Layout control name (ex: `BtnConfirmar`)
- Variable name com `&` prefix (ex: `&Alu2RegProf`)
- Attribute name

---

## P1 — Médio-impact

### #6 — `genexus_search_source` timeouts + `cancel` quebrado
**Sintoma:** busca por `P_RSENHA` (pattern simples) timeoutou 3x. `lifecycle action=cancel target=op:<id>` (com id reportado segundos antes) retornou `Task ID not found`. Worker fica com query rodando.
**Sugestão:**
- Cancel deve funcionar — padronizar lifetime do `operationId` (ex: 5min, não expirar antes).
- Index search via Lucene/ripgrep em vez de scan dinâmico (KBs reais têm milhares de objetos).

### #7 — `lifecycle action=cancel` simplesmente não cancela
**Sintoma:** chamado imediatamente após op iniciar — `Task ID not found`. Bloqueador pra recuperar workers de queries pesadas.
**Sugestão:** implementar cancel real. Se ainda não é suportado, falhar com mensagem explícita "cancel not supported for this op type" em vez de "not found".

### #8 — `genexus_inspect include=["controls"]` retorna `[]` sempre
**Sintoma:** `controls: []` para webpanels com layout XML rico (várias gxTextBlock, gxAttribute, gxButton). Tentei usar pra achar control por nome em vez de parsing XML manual.
**Sugestão:** parse o XML do Layout e popular `controls` com `{id, type, refersTo, parentControl}`.

### #9 — Worker disconnect mid-edit deixa operationId órfão
**Sintoma:** worker timeoutou + caiu durante `genexus_edit`. Após `/mcp` reconnect, `lifecycle action=result target=op:<id>` retornou `NotFound`. Tive que re-ler o arquivo pra ver se o edit aplicou ou não.
**Sugestão:**
- Persistir op state em arquivo/SQLite com TTL ≥10min.
- Cliente: auto-retry transparente após reconnect dentro do mesmo turno.

### #10 — Build status long-poll: `wait_seconds=25` força 3 polls em build de 60s
**Sintoma:** cada poll = 1 turno. Build típico de 50-70s ⇒ 3 chamadas seguidas de `lifecycle action=status` retornando `Running`. Queima tokens e tempo.
**Sugestão:** suportar `wait_seconds` até 90s. Ou flag `wait_until_terminal=true` que faz long-poll até completar (com max).

---

## P2 — Quality-of-life

### #11 — `genexus_edit mode=ops` schema indocumentado
**Sintoma:** schema mostra `ops` como array de `{op: string}`. Zero indicação de quais `op` values são válidos. Não usei o modo nesta sessão.
**Sugestão:** adicionar enum no schema: `op: "AddControl" | "MoveControl" | "SetProperty" | "RemoveControl" | ...`. Ou `genexus_edit action=help` listar ops.

### #12 — Build output verbose esconde sinais
**Sintoma:** `TailLines` tem 30+ linhas de "Copiando Módulo X" (35 módulos GeneXus) antes de "0 Erros". Quando há erro real (`CSxxxx`), fica no meio. `summary` ajuda mas só pra success.
**Sugestão:** quando `ErrorCount > 0`, filtrar TailLines pra mostrar só linhas com `error CSxxxx:` ou contendo "FAILED". Suprimir copy-module noise sempre (ou colocar atrás de flag `verbose=true`).

### #13 — `genexus_read part="Variables"` mostra texto sem IDs
**Sintoma:** retorna `&modulo : CHARACTER(1)\r\n&alu2anocad : NUMERIC(2)\r\n...` sem indicar o internal var ID. (Mesma raiz de #1, mas via outro tool.)
**Sugestão:** adicionar parte `VariableMetadata` ou flag `withInternalIds=true` que retorne `[{name: "Alu2RegProf", type: "CHARACTER(40)", internalId: 8, controlType: "Radio Button", controlValues: "..."}]`.

### #14 — `Description` é title bar do popup mas não está documentado nessa role
**Sintoma:** popup mostra `Description` property no title bar (`"Reg Prof Aluno UGPopup"` literalmente). Não-óbvio que é onde se muda — só descobri por tentativa via `genexus_properties set propertyName=Description`.
**Sugestão:** na descrição da property `Description` no schema/tool help, adicionar: "(usado como title bar quando aberto via `.Popup()`)".

### #15 — Sem warning quando elementos non-prefixed aparecem no Layout
**Sintoma:** `<Button>`, `<Bitmap>`, `<TextBlock>` (sem prefix `gx`) passam build sem warning mas viram HTML literal sem handlers. Custou ~3 iterações de build (~3 min) pra descobrir que precisava ser `<gxButton>`.
**Sugestão:** linter no `genexus_edit`/`build` validar Layout: warning quando elemento esperado-prefixado aparece sem prefix. Mensagem tipo "Unknown element `<Button>` — did you mean `<gxButton>`?".

### #16 — `genexus_edit mode=patch` com `patch={find,replace}` JSON sempre falha
**Sintoma:** Tentei `mode=patch, patch={"find":"...", "replace":"..."}` (formato JSON que o schema sugere aceitar). Resposta: `'context' (old_string) is required for Replace`. Só funciona com `operation=Replace, context=..., content=...` (legacy string format). Schema descreve as duas formas mas só uma roda.
**Sugestão:** ou implementar parsing do JSON `{find,replace}` (mais ergonômico), ou remover do schema/docs.

### #17 — Context matching de patch é whitespace-strict; falha em tab vs space
**Sintoma:** Patch context que eu escrevi com 1 indent diferente do arquivo (espaços vs tabs) deu `Context block not found` mesmo com o texto idêntico semanticamente. Tive que re-ler com `genexus_read` cada vez e copiar tabs exatos.
**Sugestão:** normalizar whitespace na comparação (tabs → spaces equivalentes via tabSize), ou fazer fuzzy match com confidence score. Quando falha, mensagem mostrar diff entre `context` provido e o trecho mais próximo encontrado — não só "not found".

### #18 — Patch `Context block not found` não mostra near-match
**Sintoma:** Erro genérico, sem indicação de onde no arquivo o context quase casou. Pra corrigir tive que re-ler todo o arquivo e diff manual.
**Sugestão:** quando patch falha, response incluir top 3 candidatos com Levenshtein distance + linha onde aparecem. Cliente ajusta context em 1 iteração.

### #21 — `<gxButton onClickEvent="...">` é silently ignorado; só `Event Enter` funciona
**Sintoma:** `<gxButton id="X" onClickEvent="'DoMyEvent'" caption="..." />` em layout HTML não bindou ao evento `'DoMyEvent'`. O .js gerado mapeou o botão pra `evt:"e1520q1_client", std:"ENTER"` (evento padrão Enter). Server-side, sem `Event Enter` definido, clicar no botão não disparava nada — POST acontecia mas `_EventName=ENTER` ⇒ no-op. Tentei `eventGX="'DoConfirmar'"` também — mesma coisa. Solução: **renomear o handler pra `Event Enter`** no source. Aí gxButton auto-bind funciona.
**Sugestão:**
- Linter: detectar `<gxButton onClickEvent="'X'" />` quando não existe `Event X` no source. Avisar: "gxButton em layout HTML só dispara `Event Enter` — onClickEvent é ignorado. Renomeie seu handler ou use `<gxBitmap eventGX="...">`".
- Documentar: gxButton em layout HTML = ENTER hardcoded; pra eventos custom, usar `<gxBitmap>` ou `<action>` em layout responsive.

### #20 — `out:` em parm rule torna variáveis disabled silenciosamente
**Sintoma:** Declarar `parm(in: ..., out: &Alu2RegProf, out: &Alu2NumRegProf)` faz GeneXus gerar `gx_radio_ctrl(..., enabled=0, readonly=1, ...)` no .cs. Inputs renderizam com `disabled=true`, usuário não consegue interagir. Comparei com dani (radio funciona) → mesmo helper, diferença está nas flags 6-8. Workaround: `&Var.Enabled = 1` explícito em Event Start.
**Sugestão:** linter detectar `out:` em parm rule sem `.Enabled = 1` correspondente em Event Start e avisar "out parm var pode renderizar disabled — adicionar `&Var.Enabled = 1` se for editável no form".

### #19 — `lifecycle status` retorna `Output` (full build log) em cada poll
**Sintoma:** Cada `lifecycle action=status` durante build long-poll retorna o `Output` completo (até 200+ linhas) — mesmo conteúdo a cada poll. 3 polls por build = log inteiro mandado 3x. Queima context/tokens.
**Sugestão:** `Output` só preencher em estado terminal (`Succeeded`/`Failed`). Em `Running`, devolver só `TailLines` (já existe e é bem menor). Ou flag `includeOutput=false` (default true) pra opt-out.
