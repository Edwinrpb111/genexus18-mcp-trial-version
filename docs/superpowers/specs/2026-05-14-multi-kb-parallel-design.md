# Multi-KB Parallel Support — Design

**Status:** Draft (auto-approved by goal directive — user delegated decisions)
**Author:** Claude (session 2026-05-14)
**Goal:** Permitir múltiplas KBs abertas simultaneamente com 100% das funcionalidades atuais. LLM que usa o MCP consegue interagir com 2+ KBs em paralelo sem aguardar uma terminar antes da outra.

## Context

Hoje (v2.2.0) o servidor MCP atende uma única KB:

- `config.json` tem `Environment.KBPath` (string, uma KB).
- `Gateway` mantém um único `WorkerProcess _worker`.
- `Worker` carrega a SDK do GeneXus (singletons globais: `ContextService`, `KBFactory`, `CommonServices`, `UIServices`) e expõe `KbService._kb` como **estático**.
- Cada chamada de tool serializa pela thread STA (WinForms bridge).

A SDK do GeneXus **não suporta múltiplas KBs no mesmo AppDomain** — `ContextService.Initialize()` e `KBFactory` são singletons que assumem 1 KB. Tentar abrir 2 KBs num único Worker corrompe estado.

**Conclusão:** paralelismo real exige **N processos Worker** (1 por KB).

## Goals / Non-Goals

**Goals**
- Cliente MCP pode chamar `tools/call` com parâmetro `kb` indicando qual KB.
- Chamadas a KBs distintas executam em paralelo (não há serialização cross-KB).
- 100% das tools atuais funcionam por KB.
- Backward-compat: configs single-KB existentes continuam funcionando sem mudança.
- Tools são auto-descritivas: `kb` aparece no schema, opcional.

**Non-Goals**
- Chamadas concorrentes para a **mesma** KB continuam serializadas (limitação da SDK; está fora do escopo paralelismo intra-KB).
- Não há sincronização cross-KB (KB A editar KB B em transação).
- Não vai suportar mais de `MaxOpenKbs` (default 3) KBs simultâneas — exceder devolve erro `KB_POOL_FULL`.

## Decisions

| Decisão | Valor | Justificativa |
|---|---|---|
| Pool model | Dinâmico on-demand, max 3 (`Server.MaxOpenKbs`), LRU eviction quando idle timeout expira ou pool cheio | Mais flexível que pool fixo; idle timeout (5 min default) já existe |
| KB identifier | Alias primário + path fallback on-demand | Aliases são humanamente legíveis; path absoluto cobre KBs ad-hoc |
| Default KB resolution | Se 1 aberta → usa ela; se 0 e `DefaultKb` no config → abre; se 2+ sem `kb` param → erro `KB_AMBIGUOUS` | Backward-compat total + segurança em caso ambíguo |
| Backward-compat | `Environment.KBPath` legado vira `DefaultKb` automaticamente | Configs antigas funcionam sem migração manual |
| `genexus_open_kb` | Aceita `path` e `alias` opcionais. Sem alias, gera de `Path.GetFileName(path)` | Permite KBs runtime |
| Idempotency cache | Chave passa a incluir `kb_alias` | Caches isolados |
| Tool `kb` param | Opcional em **todas** as tools (exceto meta: `genexus_whoami`, `genexus_logs`) | LLM seleciona explicitamente |
| Cross-KB ops | Bloqueados explicitamente em tools de write — uma chamada = uma KB | Evita estado inconsistente |

## Architecture

### High-level

```
                   ┌──────────────────────────────────────────────┐
                   │  Gateway (single process, single AppDomain)  │
                   │                                              │
   MCP client ───► │  HTTP/stdio → McpRouter → WorkerPool         │
                   │                              │                │
                   │                              ▼                │
                   │           ┌─────────────────────────────┐    │
                   │           │  WorkerPool (keyed by alias)│    │
                   │           │   ─ workerA (KB customer)   │────┼─► Worker process A
                   │           │   ─ workerB (KB order)      │────┼─► Worker process B
                   │           │   ─ workerC (KB legacy)     │────┼─► Worker process C
                   │           └─────────────────────────────┘    │
                   └──────────────────────────────────────────────┘
```

Cada `WorkerProcess` continua sendo o que é hoje (1 processo `GxMcp.Worker.exe`, 1 KB carregada). Mudança principal: `WorkerPool` no Gateway que mantém um dicionário `alias -> WorkerProcess` + ciclo de vida (idle eviction, LRU).

### Components

#### `WorkerPool` (novo, Gateway)
- Localização: `src/GxMcp.Gateway/WorkerPool.cs`
- Responsabilidade: gerenciar N `WorkerProcess`, indexar por `alias`.
- API:
  ```csharp
  public class WorkerPool {
      Task<WorkerProcess> AcquireAsync(KbHandle kb, CancellationToken ct);
      bool TryEvictIdle();           // chamado pelo loop de cleanup
      void Stop();
      KbHandle? ResolveKb(string? kbArg, Configuration config);  // alias/path → handle
      IReadOnlyList<KbHandle> ListOpen();
  }
  public record KbHandle(string Alias, string Path);
  ```
- Implementação: `ConcurrentDictionary<string,WorkerEntry>` (key = alias normalizado lowercase).
- Eviction: quando pool cheio E nova KB requisitada, remove o WorkerProcess com `_lastActivityUtc` mais antigo desde que `inFlightCommands == 0`. Se todos estão ocupados, retorna erro `KB_POOL_FULL`.

#### `KbResolver` (novo, Gateway)
- Localização: `src/GxMcp.Gateway/KbResolver.cs`
- Recebe `kb` arg + configuração e devolve `KbHandle`. Regras:
  1. Se `kb` é null/vazio: aplica regra default (única aberta OU DefaultKb OU erro `KB_AMBIGUOUS`).
  2. Se `kb` casa com um alias declarado no config ou previamente registrado → usa.
  3. Senão, se `kb` é path absoluto existente → registra com alias auto-gerado de `Path.GetFileName(kb)`.
  4. Senão → `KB_NOT_FOUND`.

#### `WorkerProcess` (modificado, Gateway)
Mudanças mínimas:
- Construtor agora recebe `KbHandle` em vez de só `Configuration`.
- `Start()` usa `kb.Path` em vez de `_config.Environment.KBPath`.
- Cada instância já tem seu próprio `_commandChannel`, `_pendingRequests` (NÃO — `_pendingRequests` é global no Gateway, OK, request IDs são GUIDs).
- Idle eviction: já existe (`ShouldStopForIdle`); reutiliza.

#### `KbService` (modificado, Worker)
Single change: tornar instance-based em vez de static.
- `private dynamic _kb` (não static)
- `_kbLock` instance field
- `_isOpenInProgress` instance field
- Como cada Worker process tem sua própria instância de `CommandDispatcher.Instance` (que injeta `KbService`), não há colisão. Os 19 services consumidores não mudam (já usam `_kbService.GetKB()`).

#### `Program.cs` (Gateway, modificado)
- Substituir `_worker` por `_workerPool`.
- `SendWorkerCommandAsync` recebe `KbHandle` extra; routeia para o worker certo via `_workerPool.AcquireAsync(kb)`.
- `_pendingRequests` continua global; request IDs já são GUIDs únicos.
- `_idempotencyCache` key fica `{kb_alias}::{original_key}`.
- `RestartWorker` vira `RestartWorker(KbHandle)` — afeta só aquele worker.

#### `McpRouter` (modificado)
- Cada router (`SearchRouter`, `ObjectRouter`, etc.) lê `kb` de `args` e propaga.
- Helper `KbResolver.Resolve(args, config)` chamado **uma vez** no topo do dispatcher (`ProcessMcpRequest`), antes de rotear.
- Resolved `KbHandle` é colocado em `workerCommand["_kb"]` (campo interno, não vai pro Worker).

### Config schema

```json
{
  "GeneXus": { "InstallationPath": "...", "WorkerExecutable": "..." },
  "Server": {
    "HttpPort": 5000,
    "WorkerIdleTimeoutMinutes": 5,
    "MaxOpenKbs": 3,
    "...": "..."
  },
  "Environment": {
    "DefaultKb": "customer",
    "KBs": [
      { "alias": "customer", "path": "C:/KBs/CustomerKB" },
      { "alias": "order",    "path": "C:/KBs/OrderKB" }
    ],
    "KBPath": "C:/KBs/CustomerKB"  // LEGACY: auto-migrado para KBs+DefaultKb se KBs ausente
  }
}
```

**Backward-compat (KBPath legacy):**
Se `Environment.KBs` ausente E `Environment.KBPath` presente, no startup:
- Gera alias = `Path.GetFileName(KBPath).ToLowerInvariant()`.
- Cria `KBs: [{alias, path: KBPath}]`.
- `DefaultKb = alias`.

### Tool schema changes

Toda tool em `tool_definitions.json` ganha um campo:
```json
"kb": {
  "type": "string",
  "description": "Optional KB alias or absolute path. If omitted and only one KB is open, uses it; if multiple KBs are open, an explicit value is required."
}
```

Exceções (sem `kb`, são globais): `genexus_whoami`, `genexus_logs`, `genexus_doc`.

Tool nova: `genexus_kb` (substitui/complementa `genexus_open_kb`):
- `action: "open"` — abre KB nova (path obrigatório, alias opcional)
- `action: "close"` — fecha KB
- `action: "list"` — lista KBs abertas com status
- `action: "set_default"` — muda default

### Error codes (novos)

| Code | Meaning |
|---|---|
| `KB_AMBIGUOUS` | Múltiplas KBs abertas, `kb` param obrigatório |
| `KB_NOT_FOUND` | Alias não existe e path não é válido |
| `KB_POOL_FULL` | `MaxOpenKbs` atingido, todos workers ocupados |
| `KB_BUSY_OPENING` | Worker em handshake de abertura — retry em 2s |

### Idempotency & operation tracking

- `IdempotencyCache.Compose(key, kbAlias) = $"{kbAlias}::{key}"` — caches isolados.
- `OperationTracker` adiciona campo `kbAlias` em cada op (visível em `genexus_logs`).

### Notifications

Atualmente `notifications/resources/updated` é broadcast. Passa a incluir `kbAlias` no payload para o cliente saber qual KB mudou.

## Data Flow (exemplo: chamadas paralelas)

```
T0  LLM ───► tools/call kb=customer genexus_list_objects(type=Procedure)
T1  LLM ───► tools/call kb=order    genexus_list_objects(type=Transaction)   # paralelo, mesmo client
        │
        ▼
   Gateway.ProcessMcpRequest (ambas)
        │
        ├─ KbResolver.Resolve("customer") → handleA → Pool.Acquire(handleA) → WorkerProcess_A
        ├─ KbResolver.Resolve("order")    → handleB → Pool.Acquire(handleB) → WorkerProcess_B
        │
        ├─ SendCommandAsync(workerA, cmd1)  ─┐
        └─ SendCommandAsync(workerB, cmd2)  ─┤  (independentes; sem lock cross-worker)
                                              ▼
            _pendingRequests[guid1] resolve quando workerA responde
            _pendingRequests[guid2] resolve quando workerB responde
```

Cada `WorkerProcess` tem seu próprio `_commandChannel` e `ProcessQueueAsync` — sem contenção entre eles. Worker A não bloqueia Worker B.

## Concurrency invariants

1. **Intra-KB:** serializado pela STA thread do Worker correspondente. (Atual.)
2. **Inter-KB:** totalmente paralelo. (Novo.)
3. **Pool acquisition:** `ConcurrentDictionary.GetOrAdd` + lock por alias durante spawn (não global).
4. **Eviction:** atômica via `Interlocked.CompareExchange` no entry state (`Alive` → `Evicting`).

## Error handling

- **Worker crashes:** apenas requests pendentes daquele worker são abortados (filtrar `_pendingRequests` por `workerAlias`). Demais KBs continuam.
- **Open fails:** retorna erro estruturado ao cliente; entry removido do pool.
- **Pool full:** `KB_POOL_FULL` com lista de KBs abertas e sugestão `"close one with genexus_kb action=close before opening another"`.

## Testing strategy

1. **Unit tests** (`GxMcp.Gateway.Tests`):
   - `KbResolver`: cada caminho de resolução (alias, path, default, ambíguo, not found).
   - `WorkerPool`: spawn, acquire, eviction LRU, pool full.
   - `IdempotencyCache`: chaves separadas por kbAlias.
2. **Integration tests** (`GxMcp.Worker.Tests`):
   - `KbService` instance isolation (criar 2 instâncias, confirmar `_kb` independente).
3. **End-to-end (manual smoke):**
   - Spawn 2 KBs, list em paralelo, medir wallclock vs serial.
   - Backward-compat: rodar com config v2.2.0 antigo, garantir funcionamento.
   - Pool full + eviction.

## Backward compatibility

- Configs sem `KBs` array continuam funcionando (legacy path).
- Chamadas sem `kb` param: enquanto só 1 KB aberta, comportamento idêntico ao atual.
- `genexus_open_kb` existente continua funcionando (mas agora pode registrar múltiplas).
- Tool definitions: adicionar `kb` é puramente aditivo, não quebra clientes velhos.

## Rollout

1. Implementar pool/resolver atrás de feature flag `Server.MultiKbEnabled` (default true para new installs, false se config tem `KBPath` legacy e nenhum `KBs`).
2. Smoke local com 2 KBs reais.
3. Bump versão para 2.3.0 (minor — feature aditiva, compat preservada).

## Out of scope (futuro)

- KB-to-KB references (cross-KB rename, import).
- Worker hot-reload sem matar processo.
- Compartilhamento de cache entre Workers da mesma KB (não há, hoje, 1:1).
- Pool elástico baseado em memória disponível.

## Open questions

(Nenhuma — todas decisões fixadas neste doc.)
