# Spec: LegalProcessMonitor SaaS Multi-Tenant & LegalOps Suite

## Capability Map

| Module id | Responsibility | Depends on |
|---|---|---|
| `tenant-workspace-persistence` | Isolamento Multi-Tenant por Escritório (`TenantId`), persistência relacional durável (SQLite WAL / DB) para Processos, Watchlist, Notas Internas de Juízo e Configurações do Escritório | — |
| `deadline-suspensions-ics` | Suspensões locais/avulsas de expediente forense (provimentos/portarias locais por comarca/escritório) integradas à Calculadora CPC/2015 + Exportador RFC 5545 (`.ics`) com alarmes D-2 e D-1 | `tenant-workspace-persistence` |
| `kanban-legal-tasks` | Quadro Kanban de Tarefas e Prazos Processuais (`Intimação Recebida` → `Em Elaboração` → `Em Revisão` → `Protocolado`) vinculado a CNJ, responsável, prazo fatal e número de protocolo | `tenant-workspace-persistence`, `deadline-suspensions-ics` |
| `djen-radar-alerts` | Radar Proativo Diário do DJEN por OAB do Escritório + Despacho de Alertas via Webhook (Telegram / Slack / Webhook Corporativo) para novas intimações | `tenant-workspace-persistence` |

**Build order:** `tenant-workspace-persistence` → `deadline-suspensions-ics` → `kanban-legal-tasks` → `djen-radar-alerts`

---

## 1. Objective
Transformar o **LegalProcessMonitor** em uma plataforma SaaS Multi-Tenant de LegalOps pronta para múltiplos escritórios de advocacia, eliminando perda de estado em memória, garantindo isolamento lógico de dados por escritório (`TenantId`), permitindo cadastro de suspensões forenses locais e exportação de prazos para agenda (`.ics`), gestão de fluxo de trabalho em Kanban processual e monitoramento proativo de intimações do DJEN por OAB com alertas externos.

## 2. Tech Stack
- **Backend:** .NET 9 Web API (C# 13, Clean Architecture, `Microsoft.Data.Sqlite` em modo WAL para persistência relacional zero-config + suporte a MongoDB/PostgreSQL, Polly v8, Rate Limiting nativo)
- **Frontend:** Angular 21 (Standalone Components, Signals, TailwindCSS, testes nativos com `node --test`)
- **Integrações Oficiais:** CNJ DataJud API, CNJ DJEN (`comunicaapi.pje.jus.br`), TJSP e-SAJ 1G/2G

## 3. Commands
```bash
# Backend Build & Test
dotnet build LegalProcessMonitor.sln
dotnet test tests/LegalProcessMonitor.Tests/LegalProcessMonitor.Tests.csproj

# Frontend Test & Production Build
cd frontend && npx tsx --test src/app/utils/calculadora-prazo.spec.ts
cd frontend && npm run build
```

## 4. Project Structure
```text
src/
├── LegalProcessMonitor.Domain/
│   ├── Entities/          # Processo, WorkspaceEscritorio, TarefaProcessual, SuspensaoForense, AlertaDjen
│   ├── Interfaces/        # IWorkspaceRepository, IProcessoRepository, IDjenComunicacaoConnector
│   └── Services/          # CalculadoraPrazoForense, GeradorCalendarioIcs
├── LegalProcessMonitor.Infrastructure/
│   ├── Persistence/       # SqliteWorkspaceRepository (Multi-Tenant WAL), SqliteProcessoRepository
│   └── Connectors/        # Djen, DataJud, Esaj, WebhookAlertDispatcher
└── LegalProcessMonitor.Api/
    └── Controllers/       # WorkspacesController, ProcessosController, AdvogadosController, MagistradosController
tests/
└── LegalProcessMonitor.Tests/ # Testes unitários e de integração SQLite/Domínio
frontend/
└── src/app/               # Aplicação Angular 21 com seletor Multi-Tenant, Kanban, Radar DJEN e Exportador .ICS
```

## 5. Code Style & Multi-Tenant Contract
Todas as operações sensíveis de escritório recebem o cabeçalho `X-Tenant-Id` (ou parâmetro de rota/query validado por regex `^[a-zA-Z0-9_-]{3,64}$`), garantindo que a Watchlist, as Notas de Juízo, as Suspensões Locais, o Quadro Kanban e o Radar OAB de um escritório jamais vazem para outro tenant.

```csharp
public sealed record TarefaProcessual(
    string Id,
    string TenantId,
    string NumeroProcesso,
    string Titulo,
    string Responsavel,
    StatusTarefaColuna Coluna,
    DateOnly PrazoFatal,
    string? NumeroProtocolo,
    DateTime CriadoEmUtc,
    DateTime AtualizadoEmUtc);
```

## 6. Testing Strategy
- **TDD Obrigatório (RED → GREEN → REFACTOR):** Cada módulo novo começa por testes em `LegalProcessMonitor.Tests` e `frontend/src/app/utils/*.spec.ts`.
- **Isolamento Multi-Tenant:** Testes automatizados provando que dados gravados pelo `tenant-a` não são visíveis para `tenant-b`.
- **Determinismo:** Banco SQLite temporário ou `:memory:` nos testes de repositório, sem dependência de rede externa.

## 7. Boundaries
- **Always do:** Validar `TenantId`, rodar `dotnet test` e `npm run build` a cada fatia, usar parâmetros SQL (`$tenantId`) contra SQL Injection.
- **Ask first:** Mudanças destrutivas de contrato em endpoints existentes (`/api/processos/{cnj}`).
- **Never do:** Introduzir dados mockados/fictícios como se fossem reais, vazar dados entre tenants ou concatenar strings em SQL.

## 8. Success Criteria
1. Processos consultados, Watchlist, Notas Internas de Juízo, Suspensões Forenses e Tarefas Kanban sobrevivem ao reinício da API em banco SQLite relacional isolado por `TenantId`.
2. A Calculadora de Prazos aceita suspensões locais cadastradas pelo escritório (ex.: feriado municipal do interior ou indisponibilidade do tribunal), recalcula o termo final e gera arquivo `.ics` válido (RFC 5545) com lembretes `VALARM`.
3. O Quadro Kanban permite criar, mover entre as 4 colunas (`Intimacao`, `Elaboracao`, `Revisao`, `Protocolado`), registrar número de protocolo e excluir tarefas processuais por escritório.
4. O Radar DJEN permite vincular a(s) OAB(s) do escritório, sincronizar com a API oficial do CNJ para detectar novas publicações, transformar uma intimação em Tarefa Kanban com 1 clique e disparar alerta via Webhook/Telegram.
