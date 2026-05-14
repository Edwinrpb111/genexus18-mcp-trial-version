# Auditoria preventiva de performance — Genexus18MCP

**Data:** 2026-05-14
**Escopo:** Worker (`.NET Framework 4.8`) + Gateway (`.NET 8`)
**Modo:** Read-only — sem alterações de código nesta iteração.
**Restrições:** Preservar qualidade e funcionalidade. Nada que toque lifecycle COM/STA do SDK GeneXus sem regression suite. Newtonsoft.Json é decisão intencional (não migrar para System.Text.Json sem benchmark).

---

## 1. Resumo executivo

| Componente | Alta | Média | Baixa | Total |
|------------|------|-------|-------|-------|
| Worker     | 5    | 5     | 3     | 13    |
| Gateway    | 2    | 6     | 3     | 11    |
| Build/Telemetria | 1 (gap) | 4 | 1 | 6 |
| **Total**  | **8** | **15** | **7** | **30** |

**Top-5 com maior ROI / menor risco** (recomendação de execução):

1. Logger assíncrono no Worker — fila + flush batched.
2. Timeout em `IdempotencyCache.SemaphoreSlim.WaitAsync`.
3. `SearchService.AsParallel().WithDegreeOfParallelism(N)`.
4. Sweeper periódico para `_pendingRequests` no Gateway.
5. Instrumentação mínima (Stopwatch + log estruturado) em `KB.Open` / `Object.Save` / `SearchService.Search`.

**Gap crítico transversal:** não existe baseline (BenchmarkDotNet ou harness equivalente). Toda otimização aplicada hoje é cega — incluir baseline antes de quaisquer mudanças estruturais.

---

## 2. Worker (.NET Framework 4.8)

### 2.1 Severidade alta

#### W-A1. Logger síncrono com lock + I/O em disco
- **Local:** `src/GxMcp.Worker/Helpers/Logger.cs:38-58`
- **Tipo:** I/O síncrono + lock global; ~194 call sites espalhados no Worker.
- **Por que importa:** Toda chamada `Logger.Info/Warn/Error/Debug` faz `lock (LockObj)` + `File.AppendAllText`. Em hot paths (search, bulk index, build) vira gargalo silencioso.
- **Sugestão:** Fila assíncrona (`BlockingCollection<string>` + thread dedicada) com flush batched a cada 100 ms ou N entradas. Manter fallback para `Console.Error` (já existe).
- **Risco:** Baixo. Mudança encapsulada no `Logger`. Sem dependência de ordenação temporal cross-thread e sem interação com SDK/STA.

#### W-A2. `VectorService.ComputeEmbedding` em loop de bulk index
- **Local:** `src/GxMcp.Worker/Services/IndexCacheService.cs:464`
- **Tipo:** Alocação repetitiva (`float[128]`) em hot path de indexação.
- **Por que importa:** Em KB com ~30 k objetos: ~3,8 MB alocados em `float[]`, somados a split + duplo loop de hash/normalização.
- **Sugestão:** `ArrayPool<float>.Shared` para os vetores, ou pré-alocação com reuso por thread. Tornar `_vectorService` singleton confirmado.
- **Risco:** Baixo. `VectorService` é stateless; pooling é opt-in e não muda semântica.

#### W-A3. `FlushToDisk` reserializa índice inteiro a cada throttle
- **Local:** `src/GxMcp.Worker/Services/IndexCacheService.cs:331-380`
- **Tipo:** Serialização pesada + `File.WriteAllText` sem incremental.
- **Por que importa:** Throttle de 10 s (`UpdateIndex` linha 339) → em bursts de write, multiplica I/O. KB grandes geram MBs de JSON por flush.
- **Sugestão:** Subir janela para 30 s; gravar comprimido (gzip) com extensão `.json.gz`; considerar gravação incremental (delta) para o futuro.
- **Risco:** Baixo. Já existe throttling; ampliação é configuração. Compressão exige só ajuste no leitor.

#### W-A4. N+1 em `AnalyzeService.GetReferences`
- **Local:** `src/GxMcp.Worker/Services/AnalyzeService.cs:48-60`
- **Tipo:** Loop sobre `obj.GetReferences()` chamando `kb.DesignModel.Objects.Get(reference.To)` por item.
- **Por que importa:** Procedure com 50+ chamadas → 50 round-trips ao SDK. Sob KB em SQL Server remoto, custo cresce linearmente.
- **Sugestão:** Coletar `reference.To` em lote, agrupar por tipo, fetch único onde possível. Validar se o SDK expõe API de leitura em massa antes.
- **Risco:** **Alto.** Toca diretamente o SDK COM. Requer regression suite e teste em múltiplas versões do GeneXus 18.

#### W-A5. `QueueWriter` faz `lock (_buffer)` por caractere
- **Local:** `src/GxMcp.Worker/Program.cs:341-376`
- **Tipo:** Lock contention em hot path de stdout (todo IPC passa por aqui).
- **Por que importa:** `Write(string)` itera char-a-char e cada char adquire o lock. Em respostas grandes (JSON com KBs) multiplica o custo.
- **Sugestão:** Implementar `Write(string)` com **um** lock que atua sobre o buffer inteiro; flush quando encontrar `\n`. Considerar buffer thread-local.
- **Risco:** Médio. IPC com Gateway depende do contrato `\n` como delimitador — preservar exatamente.

### 2.2 Severidade média

#### W-M1. `ObjectService.ReadCacheTtl = 20 s` muito curto
- **Local:** `src/GxMcp.Worker/Services/ObjectService.cs:29`
- **Sugestão:** Subir para 60 s + invalidação por evento (`OnObjectSaved`).
- **Risco:** Baixo (cache local; sem consistência cross-process).

#### W-M2. `FlushToDisk` fire-and-forget sem retry/log de falha
- **Local:** `src/GxMcp.Worker/Services/IndexCacheService.cs:338-343`
- **Por que importa:** Falha silenciosa → índice em disco fica stale; próximo cold-start recarrega versão velha.
- **Sugestão:** `try/catch` com `Logger.Error` + contador de falhas consecutivas exposto à telemetria.
- **Risco:** Médio (pode mascarar corrupção; adicionar monitoramento).

#### W-M3. `SearchService` sem telemetria
- **Local:** `src/GxMcp.Worker/Services/SearchService.cs:26-160`
- **Por que importa:** Hot path frequente, impossível detectar regressão sem medir.
- **Sugestão:** Stopwatch por query + log estruturado quando `> 50 ms`.
- **Risco:** Baixo.

#### W-M4. `AsParallel()` sem `WithDegreeOfParallelism`
- **Local:** `src/GxMcp.Worker/Services/SearchService.cs:139`
- **Por que importa:** PLINQ usa `Environment.ProcessorCount` por padrão; em máquinas 16+ cores explode threads e pressiona GC.
- **Sugestão:** `WithDegreeOfParallelism(Math.Min(4, ProcessorCount))`.
- **Risco:** Baixo (LINQ local).

#### W-M5. `ResolveHierarchy` sem cache
- **Local:** `src/GxMcp.Worker/Services/IndexCacheService.cs:201-296`
- **Por que importa:** O(N) walk por objeto durante bulk index; ~30 k objs × 5 pais médios = 150 k invocações.
- **Sugestão:** Dicionário `Guid → resolved tuple` com invalidação em operações de Folder/Module.
- **Risco:** Médio (precisa estratégia de invalidação).

### 2.3 Severidade baixa

#### W-B1. `GetEntryStorageKey` faz `string.Format` repetido
- **Local:** `src/GxMcp.Worker/Services/IndexCacheService.cs:114-125`
- **Sugestão:** Cachear chave no `IndexEntry` na primeira atribuição.
- **Risco:** Muito baixo.

#### W-B2. Regex inline (não compilada) em `CodeParser`
- **Local:** `src/GxMcp.Worker/Helpers/CodeParser.cs:37-74`
- **Sugestão:** Static fields com `RegexOptions.Compiled` (padrão de `BuildService.cs:28-39`).
- **Risco:** Baixo.

#### W-B3. `Thread.Sleep(100)` em loop de background queue
- **Local:** `src/GxMcp.Worker/Program.cs:194`
- **Sugestão:** `AutoResetEvent` ou `Channel<T>` para sinalização.
- **Risco:** Baixo.

### 2.4 Falsos positivos descartados (não revisitar)

- `Task.Run` em `CommandDispatcher`/`BuildService` — padrão correto via `SdkCommandQueue` (STA safety).
- `lock (_kbLock)` em `KbService` — KB é COM compartilhado, lock é obrigatório.
- Múltiplas instâncias de `VectorService` — stateless, construção O(1).
- Fallback silencioso em `Logger` — evita crash em disco cheio (intencional).

---

## 3. Gateway (.NET 8)

### 3.1 Severidade alta

#### G-A1. `WorkerPool._spawnLock` global serializa acquires
- **Local:** `src/GxMcp.Gateway/WorkerPool.cs:84-120`
- **Tipo:** Coarse-grained lock.
- **Por que importa:** Spawn é rápido mas serializa **todos** os acquires de KB. Sob N clientes concorrentes em KBs diferentes, contention cresce.
- **Sugestão:** Semáforo per-KB (`ConcurrentDictionary<string, SemaphoreSlim>`) ou padrão CAS no slot da entry.
- **Risco:** Alto — afeta lifecycle de workers. Testar concorrência ponta-a-ponta com >3 KBs simultâneos.

#### G-A2. `IdempotencyCache.WaitAsync` sem timeout
- **Local:** `src/GxMcp.Gateway/IdempotencyCache.cs:43-67`
- **Tipo:** Gate sem deadline → starvation se factory travar.
- **Por que importa:** TTL do cache é 65 min; se um worker pendurar o `factory()`, todos os requisitantes do mesmo key esperam até o timeout do tool.
- **Sugestão:** `WaitAsync(TimeSpan.FromSeconds(30), ct)` com erro estruturado em estouro.
- **Risco:** Alto (mudança no contrato de espera). Validar quem depende de bloqueio indefinido.

### 3.2 Severidade média

#### G-M1. Double-serialização em hot path
- **Local:** `src/GxMcp.Gateway/WorkerProcess.cs:99,142` e `src/GxMcp.Gateway/Program.cs:893`
- **Por que importa:** `SerializeObject` → `DeserializeObject<JObject>` → `ToString(Formatting.None)` por mensagem. Cada conversão aloca string + tokens.
- **Sugestão:** Operar direto com `JObject` quando possível; escrever no `StreamWriter` sem materializar string intermediária (`JsonTextWriter` direto sobre o stream).
- **Risco:** Médio. Tocar serialização exige cobertura de teste do protocolo MCP.

#### G-M2. `StreamWriter.Flush()` por comando
- **Local:** `src/GxMcp.Gateway/WorkerProcess.cs:171-172`
- **Sugestão:** Manter flush por mensagem se o protocolo exige (provavelmente sim para MCP framing); avaliar `AutoFlush = true` ou batch quando `_commandChannel.Reader.TryRead` drenar várias mensagens sequenciais.
- **Risco:** Médio (afeta latência percebida).

#### G-M3. `ProcessQueueAsync` processa uma msg por iteração
- **Local:** `src/GxMcp.Gateway/WorkerProcess.cs:117-215`
- **Por que importa:** Em alta vazão (100s msg/s), overhead de transição de task acumula.
- **Sugestão:** Já lê em laço interno via `TryRead` — verificar se o `lock (_processLock)` + `WaitForPipeReadyAsync` poderiam ser batched para múltiplas mensagens da mesma rodada.
- **Risco:** Médio.

#### G-M4. `KbBucket` lock único por bucket
- **Local:** `src/GxMcp.Gateway/IdempotencyCache.cs:69-127`
- **Por que importa:** Toda `TryGet`/`Put` trava o bucket inteiro.
- **Sugestão:** Sharding por hash da chave (32-256 shards) **ou** trocar `Dictionary+LinkedList` por estruturas concorrentes (`ConcurrentDictionary` + LRU separado).
- **Risco:** Médio.

#### G-M5. `_pendingRequests` cleanup lazy
- **Local:** `src/GxMcp.Gateway/Program.cs:64` (declaração) + retention 65 min
- **Por que importa:** Memory leak silencioso se timeouts forem frequentes.
- **Sugestão:** Sweeper periódico (`PeriodicTimer` a cada 5 min) que remove entradas vencidas.
- **Risco:** Baixo.

#### G-M6. `Thread.Sleep(1000)` em retry × 10
- **Local:** `src/GxMcp.Gateway/WorkerProcess.cs:481`
- **Por que importa:** Em workers temporariamente ocupados, retry síncrono adiciona 10 s de latência total.
- **Sugestão:** `Task.Delay` com backoff exponencial (100, 200, 400…) + jitter.
- **Risco:** Baixo.

### 3.3 Severidade baixa

#### G-B1. `SelectVictim()` ordena lista inteira
- **Local:** `src/GxMcp.Gateway/WorkerPool.cs:142-148`
- **Sugestão:** Manter min-heap por `LastActivityUtc`. Hoje o impacto é mínimo (MaxOpenKbs=3).
- **Risco:** Baixo.

#### G-B2. `ResponseSizeGuard.ByteSize()` aloca `CountingStream + JsonTextWriter`
- **Local:** `src/GxMcp.Gateway/ResponseSizeGuard.cs:50-61`
- **Sugestão:** Quando já há string serializada disponível, usar `Encoding.UTF8.GetByteCount`. Para os demais casos, considerar `ArrayPool<byte>`.
- **Risco:** Baixo.

#### G-B3. `tool_definitions.json` sem reload
- **Local:** `src/GxMcp.Gateway/McpRouter.cs:95-116`
- **Sugestão:** `FileSystemWatcher` para hot-reload em dev (nice-to-have).
- **Risco:** Muito baixo.

### 3.4 Falsos positivos descartados

- Nenhum `.Result`/`.GetAwaiter().GetResult()` em hot path — código é genuinamente async.
- Manter Newtonsoft.Json (decisão intencional pela flexibilidade do `JToken`).
- `Task.Delay(-1)` em loops de warmup — aceitável fora de hot path.
- Locks `_logLock`/`_processLock` — necessários por mutação de estado.

---

## 4. Build, telemetria e gaps de medição

### 4.1 Telemetria existente
- `src/GxMcp.Gateway/OperationTracker.cs` — coleta p50/p95 por tool (256 amostras). **Não exposta** via endpoint MCP público.
- `src/GxMcp.Gateway/PerfProfile.cs` — flag `MCP_PERF_PROFILE=v1` (default) com fallback `legacy`.
- Worker: **sem `ILogger` estruturado** (só `Console.Error` + arquivo via `Logger`).
- **Sem `Activity`/`ActivitySource`** (OpenTelemetry) nos dois processos.

### 4.2 Hot paths sem instrumentação (impossível medir regressão)
- Worker: `KB.Open`, `Object.Save`, `SdtDslParser`, `SearchService.Search`.
- Gateway: `SendWorkerCommandAsync` (sem wrapper de timing).
- Cold-start: removido em `fe65eb9`; CLI `--warm` opera sem métrica de tempo.

### 4.3 Build flags
- `src/GxMcp.Gateway/GxMcp.Gateway.csproj` — falta `<PublishReadyToRun>true</PublishReadyToRun>` em Release. .NET 8 suporta sem afetar reflection.
- `src/GxMcp.Worker/GxMcp.Worker.csproj` — `net48` x86 (obrigatório pelo SDK); pouca margem em flags modernas. **Manter `AutoGenerateBindingRedirects=true`** (Assembly Hell documentado).

### 4.4 Gap crítico
- **Nenhuma suite de benchmark** (BenchmarkDotNet, load test, cold-start harness). Cada release v2.x foi calibrada "by feel". Sem baseline, qualquer otimização aplicada em seguida é cega.

---

## 5. Backlog priorizado

### Quick-wins (baixo risco, < 1 dia cada)
1. **W-A1** — Logger assíncrono (maior ROI absoluto).
2. **W-M4** — `SearchService.AsParallel().WithDegreeOfParallelism(4)`.
3. **G-A2** — Timeout em `IdempotencyCache`.
4. **G-M5** — Sweeper para `_pendingRequests`.
5. **W-M3** — Stopwatch em `SearchService.Search` + log estruturado quando `> 50 ms`.
6. **W-M2** — Try/catch + log no `FlushToDisk`.
7. **G-M6** — Backoff exponencial + jitter no retry.
8. **W-B3** — `Thread.Sleep(100)` → `AutoResetEvent`.

### Estruturais (médio risco, > 1 dia)
9. **G-A1** — Per-KB semaphore no `WorkerPool` (substitui `_spawnLock`).
10. **W-A2** — Pooling de `float[128]` em `VectorService` (`ArrayPool<float>`).
11. **G-M4** — Sharding do `KbBucket`.
12. **W-M5** — Cache em `ResolveHierarchy` com estratégia de invalidação.
13. **W-A5** — Refator do `QueueWriter` (lock por bloco em vez de por char).
14. **Baseline de benchmark** — BenchmarkDotNet mínimo: cold-start, `list 1000 objs`, `build` pequeno, `search` com KB sintético.
15. **Exportar `OperationTracker`** via endpoint MCP de diagnóstico (`mcp/diagnostics/metrics`).
16. **W-M1** — `ObjectService.ReadCacheTtl` 20 s → 60 s + invalidação por evento.

### Não atacar sem benchmark e justificativa explícita
- Migração Newtonsoft.Json → System.Text.Json em hot path (decisão intencional documentada).
- **W-A4** — Refator do N+1 em `AnalyzeService` (toca SDK COM — exige regression suite).
- Qualquer mudança no lifecycle COM/STA do SDK.
- Trimming/AOT no Worker (reflection do SDK quebraria).

---

## 6. Recomendação de baseline (pré-requisito para o backlog estrutural)

Criar `src/GxMcp.Benchmarks/` com BenchmarkDotNet medindo:

| Cenário | Medir | Alvo de referência |
|---------|-------|--------------------|
| Cold-start do Worker (init + open KB pequena) | Tempo total + alocação | Capturar atual como baseline |
| `list_objects` em KB com 1 000 objetos | Latência p50/p95 + GC gen-0/1 | Capturar atual |
| `search` simples em KB com 10 000 objetos | Latência p50/p95 | Capturar atual |
| `build` de Procedure isolada (sem chamadores) | Latência + throughput | Capturar atual |
| Bulk index de 5 000 objetos | Tempo total + alocação | Capturar atual |

Salvar resultados em `docs/benchmarks/baseline-2026-05-14.md`. Reexecutar após cada mudança do backlog estrutural e anexar comparação.

---

## 7. Histórico relevante (commits já aplicados)

| Commit | Tema | Status |
|--------|------|--------|
| `fe65eb9` | streamline cold-start UX for large KBs | aplicado |
| `48d5f10` | stop flooding stdio with operational telemetry | aplicado |
| `7d58061` | v2.2.0 — perf & tool-stability (ResponseSizeGuard, pagination) | aplicado |
| `7c7820e` | trim `tool_definitions.json` (-71% tokens) | aplicado |
| `051dff9` | `MCP_PERF_PROFILE` feature flag | aplicado |

Nenhum dos achados desta auditoria conflita com decisões registradas em `docs/dev_log.md`, `docs/technical_architecture.md`, `docs/sdk_integration_stabilization.md`.

---

## 8. Próxima ação sugerida

Aprovar este backlog e iniciar pela linha 1 (W-A1 — Logger assíncrono). Cada item vira seu próprio plano de implementação com TDD. **Não executar nada estrutural (itens 9-16) antes do baseline (item 14).**
