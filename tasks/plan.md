# Implementation Plan: LegalProcessMonitor SaaS Multi-Tenant & LegalOps Suite

## Overview
Evolução arquitetural do **LegalProcessMonitor** de um consultor single-session em memória para uma plataforma SaaS Multi-Tenant de gestão de contencioso e LegalOps. Implementa persistência relacional durável via SQLite WAL (`Microsoft.Data.Sqlite`), isolamento estrito por escritório (`TenantId`), gestão de suspensões forenses locais com exportação de prazos para agenda RFC 5545 (`.ics`), Quadro Kanban de tarefas processuais e Radar Proativo de Intimações DJEN com disparo de Webhook/Telegram.

## Architecture Decisions
1. **SQLite em Modo WAL (`Microsoft.Data.Sqlite`) para Persistência SaaS Durável:** Zero dependência de infraestrutura externa paga, transacional, ACID, suporta leituras concorrentes e persiste tanto processos quanto todo o workspace multi-tenant (`Watchlist`, `NotasJuizo`, `SuspensoesLocais`, `TarefasKanban`, `RadarOab`).
2. **Isolamento Multi-Tenant via `TenantId`:** Identificação do escritório via cabeçalho `X-Tenant-Id` (com seletor/criador de Workspace de Escritório + Chave de Acesso no Frontend), garantindo separação lógica estrita em todas as tabelas SQL.
3. **Exportador `.ICS` RFC 5545 Nativo (Backend & Frontend):** Gera eventos de calendário universais compatíveis com Google Calendar, Microsoft Outlook e Apple Calendar, incluindo `VALARM` para `D-2` (48h antes) e `D-1` (24h antes) e memória de cálculo no `DESCRIPTION`.
4. **Integração Direta entre Radar DJEN, Calculadora e Kanban:** Qualquer intimação real capturada no DJEN/CNJ pode ser convertida com 1 clique em um card no Quadro Kanban com prazo fatal pré-calculado pelo motor CPC/2015.

## Task List

### Phase 1: Foundation — Multi-Tenant Persistence (`tenant-workspace-persistence`)
- [x] Task 1: Entidades de Domínio Multi-Tenant (`WorkspaceEscritorio`, `ItemWatchlistTenant`, `NotaJuizoTenant`, `SuspensaoForenseLocal`, `TarefaProcessual`, `ConfigRadarOab`) e contrato `IWorkspaceRepository`
- [x] Task 2: Repositórios SQLite (`SqliteWorkspaceRepository` e `SqliteProcessoRepository`) com criação automática de schema WAL e testes TDD de isolamento entre tenants
- [x] Task 3: Controller REST `WorkspacesController` (`/api/workspaces`) com validação de `X-Tenant-Id`

### Checkpoint 1: Foundation
- [x] Todos os testes de persistência SQLite e isolamento multi-tenant passam (`dotnet test`)

### Phase 2: Suspensões Forenses Locais & Agenda `.ICS` (`deadline-suspensions-ics`)
- [x] Task 4: Suporte a suspensões locais customizadas no motor `CalculadoraPrazoForense` (C# e TypeScript) + Gerador RFC 5545 `.ics` (`GeradorCalendarioIcs`) com testes unitários TDD
- [x] Task 5: Endpoint `/api/processos/exportar-ics` e integração de suspensões locais e botão `.ics` / Google Calendar no Frontend

### Checkpoint 2: Deadline & Calendar
- [x] Testes unitários C# e TypeScript da calculadora com suspensões locais e geração `.ics` aprovados

### Phase 3: Quadro Kanban de Tarefas Processuais (`kanban-legal-tasks`)
- [x] Task 6: Endpoints CRUD de Tarefas Processuais no `WorkspacesController` (`Intimação` → `Elaboração` → `Revisão` → `Protocolado`) + testes
- [x] Task 7: Interface visual do Quadro Kanban no Angular 21 + criação de tarefa em 1 clique a partir da intimação/calculadora

### Checkpoint 3: Kanban LegalOps
- [x] Fluxo completo de criação, movimentação de coluna, registro de protocolo e persistência no SQLite validado

### Phase 4: Radar Proativo DJEN & Alertas Webhook (`djen-radar-alerts`)
- [x] Task 8: Serviço de sincronização de Radar DJEN por OAB do escritório + `WebhookAlertDispatcher` (Telegram / Webhook JSON) + testes unitários
- [x] Task 9: Painel de Radar DJEN & Seletor Multi-Tenant de Escritório no Frontend Angular 21 + verificação completa de build e deploy

### Checkpoint 4: Complete
- [x] 100% dos testes backend (`dotnet test`) e frontend (`node --test` + `npm run build`) passando com zero warnings/erros

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| Concorrência em arquivo SQLite | Médio | Habilitar `PRAGMA journal_mode=WAL;` e `PRAGMA busy_timeout=5000;` na abertura da conexão |
| Vazamento de dados entre escritórios (Cross-Tenant Leak) | Alto | Todas as queries exigem `WHERE TenantId = $tenantId` + suíte de testes dedicada de isolamento |
| Indisponibilidade de Webhook externo configurado pelo usuário | Baixo | Timeout curto (5s) e captura segura de erros com retorno de diagnóstico sem travar a API |
