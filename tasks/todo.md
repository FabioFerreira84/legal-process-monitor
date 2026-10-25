# Task Breakdown — LegalProcessMonitor SaaS Multi-Tenant & LegalOps Suite

## Task 1: Modelagem de Domínio Multi-Tenant e Contrato de Workspace
**Description:** Criar as entidades de domínio para isolamento por escritório (`TenantId`): `ItemWatchlistTenant`, `NotaJuizoTenant`, `SuspensaoForenseLocal`, `TarefaProcessual` (`StatusTarefaColuna`), `ConfigRadarOab` e a interface `IWorkspaceRepository`.
**Acceptance criteria:**
- [x] Entidades imutáveis/validadas criadas em `LegalProcessMonitor.Domain`
- [x] Interface `IWorkspaceRepository` cobre Watchlist, Notas de Juízo, Suspensões Locais, Tarefas Kanban e Radar OAB por `tenantId`
**Verification:**
- [x] `dotnet build LegalProcessMonitor.sln` compila sem erros
**Dependencies:** None
**Files likely touched:**
- `src/LegalProcessMonitor.Domain/Entities/WorkspaceEntities.cs`
- `src/LegalProcessMonitor.Domain/Interfaces/IWorkspaceRepository.cs`

## Task 2: Persistência Relacional SQLite (WAL) Multi-Tenant com TDD
**Description:** Instalar `Microsoft.Data.Sqlite`, implementar `SqliteWorkspaceRepository` e `SqliteProcessoRepository` com inicialização automática das tabelas e índices, e escrever testes TDD provando persistência durável e isolamento total entre `tenant-a` e `tenant-b`.
**Acceptance criteria:**
- [x] Testes em `SqliteWorkspaceRepositoryTests.cs` e `SqliteProcessoRepositoryTests.cs` verificam CRUD e isolamento multi-tenant
- [x] Processos semeados e novos processos consultados são persistidos em SQLite
**Verification:**
- [x] `dotnet test tests/LegalProcessMonitor.Tests/LegalProcessMonitor.Tests.csproj`
**Dependencies:** Task 1
**Files likely touched:**
- `src/LegalProcessMonitor.Infrastructure/LegalProcessMonitor.Infrastructure.csproj`
- `src/LegalProcessMonitor.Infrastructure/Repositories/SqliteWorkspaceRepository.cs`
- `src/LegalProcessMonitor.Infrastructure/Repositories/SqliteProcessoRepository.cs`
- `tests/LegalProcessMonitor.Tests/SqlitePersistenceTests.cs`

## Task 3: Gerador de Calendário RFC 5545 (.ICS) & Suspensões Forenses Locais (TDD)
**Description:** Estender `CalculadoraPrazoForense` (C# e TypeScript) para receber uma coleção opcional de `SuspensaoForenseLocal` (feriados municipais/indisponibilidades de sistema cadastradas pelo escritório) e criar `GeradorCalendarioIcs` para exportar vencimentos fatais com alarmes `VALARM` D-2 e D-1.
**Acceptance criteria:**
- [x] Dias de suspensão local cadastrados pelo escritório são saltados e auditados em `DiasNaoUteisIgnorados`
- [x] `GeradorCalendarioIcs` gera arquivo `.ics` compatível com RFC 5545 contendo `VEVENT` e dois `VALARM`
**Verification:**
- [x] `dotnet test` e `npx tsx --test src/app/utils/calculadora-prazo.spec.ts` passam 100%
**Dependencies:** Task 1
**Files likely touched:**
- `src/LegalProcessMonitor.Domain/Services/CalculadoraPrazoForense.cs`
- `src/LegalProcessMonitor.Domain/Services/GeradorCalendarioIcs.cs`
- `tests/LegalProcessMonitor.Tests/CalculadoraPrazoForenseTests.cs`
- `frontend/src/app/utils/calculadora-prazo.ts`
- `frontend/src/app/utils/calculadora-prazo.spec.ts`

## Checkpoint 1: Core Domain & SQLite Persistence
- [x] Todos os testes de domínio, calculadora, `.ics` e persistência SQLite multi-tenant passando

## Task 4: API REST Multi-Tenant (`WorkspacesController`, Alertas Webhook e `.ICS`)
**Description:** Criar `WorkspacesController` (`/api/workspaces`) com suporte ao header `X-Tenant-Id` para sincronizar todo o estado do escritório (Watchlist, Notas, Suspensões, Kanban, Radar DJEN por OAB e Disparo de Webhook/Telegram) e endpoint de exportação `.ics` em `ProcessosController`.
**Acceptance criteria:**
- [x] Validação rigorosa de `X-Tenant-Id` (default seguro `escritorio-demo` se omitido em dev, isolamento estrito quando informado)
- [x] Endpoints de Kanban (`POST/PUT/DELETE /api/workspaces/tarefas`), Suspensões (`POST/DELETE /api/workspaces/suspensoes`), Watchlist, Notas e Radar DJEN funcionais
- [x] Endpoint `GET /api/processos/{numero}/exportar-ics` retorna `text/calendar`
**Verification:**
- [x] Testes de integração/controller e `dotnet test` passando
**Dependencies:** Task 2, Task 3
**Files likely touched:**
- `src/LegalProcessMonitor.Api/Controllers/WorkspacesController.cs`
- `src/LegalProcessMonitor.Api/Controllers/ProcessosController.cs`
- `src/LegalProcessMonitor.Infrastructure/Connectors/Alerts/WebhookAlertDispatcher.cs`
- `src/LegalProcessMonitor.Api/Program.cs`

## Task 5: Frontend SaaS Multi-Tenant — Workspace Switcher, Suspensões Locais, Exportação `.ICS`, Kanban LegalOps e Radar DJEN
**Description:** Integrar o Frontend Angular 21 ao backend multi-tenant (`X-Tenant-Id`), adicionando o Seletor de Escritório (Multi-Tenant Workspace), gerenciamento de Suspensões Forenses Locais + botão "Exportar para Agenda (.ICS / Google Calendar)" na Calculadora, Quadro Kanban interativo de 4 colunas (`Intimação` → `Elaboração` → `Revisão` → `Protocolado`) com criação em 1 clique a partir da intimação, e Gaveta de Radar Proativo DJEN com configuração de Webhook/Telegram.
**Acceptance criteria:**
- [x] Trocar de escritório (`TenantId`) altera imediatamente a Watchlist, Notas de Juízo, Suspensões Locais, Quadro Kanban e Radar OAB carregados do backend SQLite
- [x] Botão "Adicionar à Agenda (.ICS)" baixa arquivo `.ics` válido com os dados reais do processo e termo final calculado
- [x] Quadro Kanban permite criar tarefas a partir do prazo calculado, avançar/voltar colunas, informar número de protocolo e filtrar por status
- [x] Radar DJEN varre as publicações da OAB do escritório e permite converter qualquer intimação em Tarefa Kanban com 1 clique
**Verification:**
- [x] `npx tsx --test src/app/utils/calculadora-prazo.spec.ts`
- [x] `npm run build` sem erros ou warnings
**Dependencies:** Task 4
**Files likely touched:**
- `frontend/src/app/models/processo.model.ts`
- `frontend/src/app/services/processo.service.ts`
- `frontend/src/app/app.ts`
- `frontend/src/app/app.html`

## Checkpoint 2: Full Verification & Deployment
- [x] Suíte completa de testes backend e frontend aprovada
- [x] Build de produção limpo e deploy no GitHub Pages concluído
