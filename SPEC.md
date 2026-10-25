# Spec: LegalProcessMonitor SaaS Multi-Tenant & LegalOps Suite

## Capability Map

| Module id | Responsibility | Depends on |
|---|---|---|
| `tenant-workspace-persistence` | Isolamento Multi-Tenant por Escritório (`TenantId`), persistência relacional durável (SQLite WAL / DB) para Processos, Watchlist, Notas Internas de Juízo e Configurações do Escritório | — |
| `deadline-suspensions-ics` | Suspensões locais/avulsas de expediente forense (provimentos/portarias locais por comarca/escritório) integradas à Calculadora CPC/2015 + Exportador RFC 5545 (`.ics`) com alarmes D-2 e D-1 | `tenant-workspace-persistence` |
| `kanban-legal-tasks` | Quadro Kanban de Tarefas e Prazos Processuais (`Intimação Recebida` → `Em Elaboração` → `Em Revisão` → `Protocolado`) vinculado a CNJ, responsável, prazo fatal e número de protocolo | `tenant-workspace-persistence`, `deadline-suspensions-ics` |
| `djen-radar-alerts` | Radar Proativo Diário do DJEN por OAB do Escritório + Despacho de Alertas via Webhook (Telegram / Slack / Webhook Corporativo) para novas intimações | `tenant-workspace-persistence` |
| `multi-state-scrapers-intel` | Expansão nacional de 91 tribunais CNJ + Multi-Estado e-SAJ (`TJSP`, `TJSC`, `TJMS`, `TJCE`, `TJAM`, `TJAL`, `TJAC`), Scraper de Jurisprudência ao Vivo por Magistrado (`cjsg` + `DJEN`), Ficha Societária/Patrimonial de Partes (`BrasilAPI` + `Querido Diário`) e Importador de Feriados/Suspensões | `tenant-workspace-persistence`, `deadline-suspensions-ics` |
| `legal-theses-recommendation` | **Banco de Teses Jurídicas Multi-Tenant** (persistência SQLite WAL de teses utilizadas pelo escritório, argumentos, precedentes e histórico de aplicação) + **Motor Tático de Recomendação de Teses** (`MotorRecomendacaoTese`) que cruza o contexto real do processo (classe, assunto, último andamento/intimação e perfil do juízo) com a tese ideal a ser peticionada | `tenant-workspace-persistence`, `multi-state-scrapers-intel` |

**Build order:** `tenant-workspace-persistence` → `deadline-suspensions-ics` → `kanban-legal-tasks` → `djen-radar-alerts` → `multi-state-scrapers-intel` → `legal-theses-recommendation`

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
5. **Persistência Resiliente Cloud (`cloud-persistence-keepalive`):** Exportação/Importação atômica de Snapshot JSON completo do escritório (`GET /api/workspaces/backup` e `POST /api/workspaces/restore`), auto-mirror inteligente no navegador para recuperar instantaneamente dados após reinicialização de container gratuito (Render Free) e Keep-Alive agendado via GitHub Actions.
6. **Gerador Automático de Minuta Processual (`legal-pleading-generator`):** Geração de peça jurídica contextualizada (`POST /api/workspaces/teses/gerar-minuta`) cruzando os dados reais do processo (Órgão Julgador, Partes, Advogados, Classe, Último Andamento), o cálculo matemático de tempestividade CPC/2015 e a tese jurídica recomendada, com download direto em formato `.doc` (Microsoft Word ABNT/Forense).
7. **Calculadora de Liquidação Financeira & Honorários Bacen (`financial-calculator-bcb`):** Atualização monetária e juros de mora segundo a **Lei nº 14.905/2024 (Arts. 389 e 406 do Código Civil)** consumindo as séries históricas oficiais do Banco Central do Brasil (`SGS 433 IPCA` e `SGS 4390 SELIC`), com simulação de honorários sucumbenciais (Art. 85 CPC) e multa/honorários de cumprimento de sentença (Art. 523, § 1º CPC).
8. **Scrapers Multi-Tribunal STJ / Eproc / PJe Público (`stj-eproc-pje-scrapers`):** Enriquecimento de autos públicos e jurisprudência de magistrados para tribunais superiores (`STJ`) e sistemas `Eproc`/`PJe` via DJEN e portais públicos.
