# LegalProcessMonitor - Plano de Arquitetura & Implementação

## 1. Visão Geral
Sistema distribuído para consulta, monitoramento e captura de andamentos processuais de tribunais brasileiros, construído sobre a API pública do DataJud (CNJ) e arquitetura extensível para web scrapers (ex: TJSP e-SAJ).

## 2. Stack Tecnológica
- **Backend:** C# / .NET 9 (Clean Architecture, Worker/Services, Polly Resilience)
- **Banco de Dados:** MongoDB (Modelagem NoSQL flexível para peças e andamentos processuais)
- **Frontend:** Angular (TypeScript, RxJS, Componentes reativos, Timeline UX)
- **Testes:** xUnit, FluentAssertions, Moq

## 3. Estrutura de Módulos (Clean Architecture)
```
legal-process-monitor/
├── LegalProcessMonitor.sln
├── src/
│   ├── LegalProcessMonitor.Domain/          # Entidades, Value Objects, Regras de negócio puras
│   │   ├── ValueObjects/NumeroProcessoCnj.cs (Módulo 97 CNJ)
│   │   ├── Entities/ (Processo, Movimentacao, Envolvido)
│   │   └── Interfaces/ (ITribunalConnector, IProcessoRepository)
│   ├── LegalProcessMonitor.Infrastructure/  # Acesso a dados e chamadas externas
│   │   ├── Connectors/DataJud/ (DataJudConnector, Polly Resilience)
│   │   ├── Repositories/ (MongoProcessoRepository)
│   │   └── Configuration/ (MongoDbSettings, DataJudSettings)
│   └── LegalProcessMonitor.Api/             # ASP.NET Core Web API
│       ├── Controllers/ (ProcessosController)
│       └── Program.cs
├── tests/
│   └── LegalProcessMonitor.Tests/           # Testes unitários com TDD
└── frontend/                                # Angular SPA (Dashboard & Timeline)
```
