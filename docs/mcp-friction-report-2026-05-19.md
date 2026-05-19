# MCP Friction Report — 2026-05-19

**MCP version:** 2.5.0 | **GeneXus:** 18.0.7.179127 | **KB:** AcademicoHomolog1

Sessão real: ajustes no popup `RegProfAlunoUGPopup` da UG (Universidade Gratuita) — adicionar bloco resumo de horas (igual cabeçalho `ListaAtiCPAlunoUniGra`), responsividade mobile, e investigar radios "Possui registro?" que aparecem disabled. ~80 chamadas MCP, 6 build cycles, validação ao vivo via `chrome-devtools-axi` contra `localhost/portal3_desenv`.

Só itens **acionáveis no código do MCP**. Quirks do GeneXus runtime ficam fora.

---

## P0 — High-impact

### #1 — `gxButton onClickEvent` (custom event) ignorado pelo gerador HTML

**Sintoma:** Adicionei 3 `<gxButton>` no layout XML do popup com `OnClickEvent="'OpcSim'"` (e variantes case `onClickEvent`, `eventGX`). Build passou (0 erros). Forma renderizada mostrou todos os botões com `data-gx-evt="5"` (Enter) — exatamente como o `<gxButton id="BtnConfirmar" eventGX="'Enter'" />` original. Clicar em qualquer um disparou o Event Enter do popup, não os custom (`OpcSim`/`OpcNao`/`OpcSemConselho`). Os eventos `Event 'OpcSim'\n &Alu2RegProf = 'S'\nEndEvent` estão definidos no part `events` e compilam.

Repro:
```xml
<gxButton id="BtnOpcS" caption="Sim" onClickEvent="'OpcSim'" />
```
HTML gerado: `<input type="button" id="BTT43" data-gx-evt="5" ...>` (5 = Enter). Confirmação via DevTools: nenhum `onclick`/`data-gx-event-name` referência a `OpcSim`.

**Hipótese:** `gxButton` em `<Form id="1" type="html">` (WebForm-html) não respeita `OnClickEvent` via XML — só funciona em `<Form type="layout">` via `<action onClickEvent="...">` (que é o que `WorkWithPlus` usa em ListaAti, e funciona). O gerador parece ignorar a propriedade no XML em forms html.

**Sugestão:**
- `LayoutService.SetProperty()` detectar `gxButton.OnClickEvent` e usar a API SDK de event-wiring em vez de raw XML mutation (`element.SetAttributeValue` em LayoutService.cs:186). A IDE Properties Panel consegue setar isso corretamente — significa que existe uma rota SDK.
- Investigar `Artech.Genexus.Common.Controls.WebButton` ou similar pra encontrar o método de event-bind.
- Curto prazo: rejeitar `OnClickEvent`/`onClickEvent` em XML de gxButton (não silenciar) e instruir uso de `genexus_properties set control=BtnX propertyName=OnClickEvent value=...` se essa rota funcionar.

### #2 — `gxAttribute ControlType="Radio Button" | "Combo Box"` em `<Form type="html">` sempre renderiza disabled

**Sintoma:** Layout: `<gxAttribute AttID="var:N" ControlType="Radio Button" ReadOnly="False" />` em um popup com `<Form id="1" type="html">`. HTML renderizado: `<input type="radio" disabled="" class="gx-disabled" data-gx-readonly="" ...>` — sempre disabled. Vale o mesmo pra `ControlType="Combo Box"`. Tentativas que não funcionam:
- `ReadOnly="False"` no XML
- Sem ReadOnly
- `Enabled="True"`
- `genexus_properties set ... ReadOnly value=False`
- Trocar pra Combo Box (mesmo comportamento)

**Hipótese original (DISPROVADA):** Achei que era variável sombreando atributo de transação. Testei renomeando `&Alu2RegProf` → `&RespRegProf` (não bate com atributo nenhum) — **continuou disabled**. Logo o nome da variável é irrelevante; o gerador HTML simplesmente nunca emite radio/combo editável dentro de `<Form type="html">`.

**Causa real (confirmada):** É comportamento do gerador HTML do GeneXus. Em html-form, gxAttribute discreto (Radio/Combo) é sempre rendered como display. Em `<Form type="layout">` (table responsive WWP-style) o mesmo controle vira editável via `<action>`/`<gxControl>`. Não é gap do MCP — é design constraint do GeneXus.

**Sugestão:**
- Detecção via `LayoutGotchaScanner.GotchaGxAttributeHtmlFormDiscreteReadOnly` no `genexus_inspect.layoutGotchas`. Workaround sugerido: mover pra Form type="layout", usar User Control, ou raw HTML `<input type="radio">` em gxTextBlock Format=HTML + JS wiring de volta pra gxAttribute hidden (default ControlType, que IS editable).

---

## P1 — Medium-impact

### #3 — Mapping `var:N` (layout AttID) ↔ `internalId` (inspect) inconsistente

**Sintoma:** `genexus_inspect` retorna `variables[].internalId` (1, 2, 3, 4...). Layout XML usa `AttID="var:N"` com N opaco. Não há jeito de saber qual var:N escrever pra bind a uma variável específica.

Empiricamente descobri (depois de testar e ver o form HTML rendered):
- pos 4 `Alu2RegProf` → `var:8`
- pos 5 `Alu2NumRegProf` → `var:9`
- pos 8 `IsAlunoUniGra` → `var:12`
- pos 12 `TotalHorasCredito` → `var:16`
- pos 14 `TotalHorasDevidas480` → `var:18`

Padrão: `var:N = posição + 4`. Mas só descobri depois de errar 6 mapeamentos e bater na cabeça (var:12 → IsAlunoUniGra renderizou como checkbox quando esperava ser TotalHorasCredito). O `+4` provavelmente é o número de variáveis "system" (Pgmname, Pgmdesc, Today, Time) criadas antes pela WWP pattern, mas isso não é estável (pode ser +N pra outros popups).

Quando adicionei novas variáveis via `genexus_add_variable`, elas vieram com `internalId` 12, 13, 16, 18 (com gaps onde estavam vars Boolean). Sem o mapping correto pra layout, o `<gxAttribute AttID="var:12" />` que escrevi apontou pra IsAlunoUniGra em vez de TotalHorasCredito.

**Sugestão:**
- `genexus_inspect include=["variables"]` adicionar campo `layoutAttId` no shape de variables. Implementação: durante inspect, ler `WebFormPart.Document` e procurar `AttID="var:N"` referências; cross-referenciar com SDK property real da variável. Se o gerador GeneXus mantém esse mapeamento, expor via reflexão na Variable instance (provavelmente `Variable.Properties[id_VAR_LAYOUT_ID]` ou similar).
- Alternativa minimal: ferramenta `genexus_layout get_var_bindings name=<obj>` que devolve `{varName, attId}[]` extraído do layout XML cruzado com a lista de variáveis. Pelo menos torna o mapping descobrível.
- Atual `VariableInjector.GetVariableInternalId()` (VariableInjector.cs:173) usa fallback `idxLocal` que NÃO corresponde a var:N. Deveria retornar `null` se não achou a prop real em vez de mentir.

### #4 — `genexus_add_variable` rejeita tipos SDT

**Sintoma:** `genexus_add_variable typeName="SdtAluUniGraInfo"` retorna `{code:"UnknownType", accepted:["Character","Numeric","Boolean","Date","DateTime","Time","LongVarChar","Blob","Image","GUID"]}`. SDT é tipo válido — eu tinha que adicionar `&SDTAluInfo : SdtAluUniGraInfo` no popup pra duplicar a lógica do RetAluUniGra. Workaround: editar manualmente o part `variables` via `genexus_edit` em modo patch — funciona mas fura a abstração.

Local: `WriteService.cs:1535` chama `VariableTypeResolver.Resolve()` que só conhece primitivos. Já existe código (`WriteService.cs:1594-1596`) que chama `VariableInjector.BindVariableToSdt()` se SDK reconhecer — mas o resolver bloqueia antes.

**Sugestão:**
- `VariableTypeResolver` aceitar qualquer string e tentar resolver via KB lookup (procurar SDT/BC/Domain com nome match) antes de cair em "UnknownType".
- Se KB lookup falha, retornar `accepted` + `nearestSdtMatches: [...]` listando SDTs disponíveis.
- Padrão: `typeName` aceita `Character(40)`, `Numeric(10,2)`, `SdtFoo`, `SdtFoo.Item`, `DomainBar`.

### #5 — `inspect.controls` não popula `name` pra `gxAttribute` sem `id` attribute

**Sintoma:** Popup tem `<gxAttribute AttID="var:8" ControlType="Radio Button" />` (sem `id`). `genexus_inspect include=["controls"]` retorna entry com `{type:"gxAttribute", dataBinding:"var:8", _fallback:true}` mas SEM `name`. Logo `genexus_layout set_property control=??? propertyName=...` não tem como referenciar o controle (precisa de name/id).

Local: `UIService.cs:116` faz `ctrlName = node.Attributes?["ControlName"]?.Value ?? node.Attributes?["id"]?.Value` — falha quando nenhum dos dois existe. Resultado: `name=null` no JSON output, mas o entry é retornado mesmo assim (linha 125 só skipa se ambos ctrlName E binding forem vazios).

**Sugestão:**
- Quando `name` está vazio, ainda assim popular com algo derivável: `name = $"gxAttribute@{dataBinding}"` ou `name = $"{type}#{index}"`. Permite ao agente referenciar o controle.
- Adicionar campo `boundVariableName` quando `dataBinding` for `var:N` e o mapping for resolvível (depende do #3).
- `genexus_layout set_property control=<dataBinding>` aceitar também o `var:N` como identificador alternativo.

---

## Resumo / impacto

Esses 5 gaps combinados custaram ~40 minutos de debug por turn no fim da sessão (testes empíricos pra descobrir o mapping `var:N`, tentativas múltiplas com `ReadOnly`/`Enabled`/`OnClickEvent`). O caminho que funcionou no final foi **abrir o IDE e setar manualmente** — exatamente o que o MCP tenta evitar.

Priorização sugerida:
- **P0** #1 e #2: bloqueiam casos comuns (forms com eventos custom; variáveis que reaproveitam nomes de atributo). Sem fix, o agente fica preso e tem que delegar pro humano.
- **P1** #3 #4 #5: causam friction mas têm workaround (editar variables raw, guessar var:N, fechar olhos pro name). Importantes mas não bloqueadores.

Cada um exige investigação dedicada do SDK Artech/GeneXus — não é refactor trivial. Estimo 1 dia de dev por gap P0, meio dia por gap P1.

---

## Status do fix (v2.5.1 — 2026-05-19)

| Gap | Status | Detalhe |
|---|---|---|
| #1 OnClickEvent | **Detected (warning)** | Limitação do GeneXus HTML generator confirmada (não é gap do MCP). Scanner `LayoutGotchaScanner.cs` agora emite `GotchaGxButtonHtmlFormCustomEvent` em `genexus_inspect.layoutGotchas` quando o pattern é detectado, com workaround sugerido. Build+smoke cycle eliminado. |
| #2 Radio/Combo disabled em html-form | **Detected (warning)** | `GotchaGxAttributeHtmlFormDiscreteReadOnly` emitido quando `gxAttribute ControlType=Radio Button|Combo Box` em `<Form type="html">`. Hipótese original do shadow estava errada — confirmado live com rename de variável. É limitação do gerador HTML, não do MCP. Workaround: Form type="layout", User Control, ou raw HTML radios via gxTextBlock Format=HTML. |
| #3 layoutAttId mapping | **Fixed** | `Variable.Id` (C# instance property) é o `var:N` real do layout. Acessado via reflexão em `GetVariableInternalId` — `GetPropertyValue("Id")` retorna null porque consulta o Properties bag, não os props C#. Confirmado live: `TotalHorasCredito.Id=22` ↔ `var:22`, `SaldoHoras.Id=33` ↔ `var:33`, system vars=1-4. Fallbacks (bag, posição) mantidos por resiliência. |
| #4 SDT em add_variable | **Fixed** | Resolver agora aceita bare names (`SdtFoo`, BC, Domain) e roteia via `ResolveTypeObject`. KB lookup ausente → `UnknownType` com mensagem clara. Tests: `VariableTypeResolverTests`. |
| #5 name em gxAttribute | **Fixed** | Fallback sintético `gxAttribute@{dataBinding}` em `UIService.cs`. |

Tests: `dotnet test src/GxMcp.Worker.Tests` 342/342 passa. Gateway 252/252 passa.

Bônus do fix #3: a resolução `var:N → variável` agora é confiável em todos os helpers
(WebFormSchemaHints, LayoutGotchaScanner) — antes o fallback posicional confundia nomes
em objetos WWP+ que injetam system vars cedo.
