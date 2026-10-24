# ⚖️ LegalProcess Monitor

[![Live Demo](https://img.shields.io/badge/Demo-GitHub%20Pages-brightgreen.svg)](https://fabioferreira84.github.io/legal-process-monitor/)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)
[![Angular](https://img.shields.io/badge/Angular-21-red.svg)](https://angular.dev/)
[![MongoDB](https://img.shields.io/badge/MongoDB-NoSQL-green.svg)](https://www.mongodb.com/)
[![Resilience](https://img.shields.io/badge/Resilience-Polly-blue.svg)](https://github.com/App-vNext/Polly)
[![TDD](https://img.shields.io/badge/Tests-xUnit%20%26%20FluentAssertions-success.svg)]()

> 🌐 **Acesso Online (Live Demo):** [https://fabioferreira84.github.io/legal-process-monitor/](https://fabioferreira84.github.io/legal-process-monitor/)

Sistema distribuído e escalável para consulta, normalização e monitoramento de andamentos processuais de tribunais brasileiros, integrando a **API Oficial Pública do DataJud (CNJ)** e arquitetura extensível para web scrapers (ex: TJSP e-SAJ).

---

## 🎯 Por que este projeto foi construído?

A captura e acompanhamento de dados judiciais no Brasil é um dos desafios mais complexos de engenharia de software devido à fragmentação de sistemas (e-SAJ, PJe, Eproc, Projudi), instabilidades transitórias de rede e volumetria massiva de eventos não estruturados.

Este projeto resolve essa dor através de:
1. **Validação Matemática Rígida:** Value Object com implementação do algoritmo **Módulo 97 da Resolução CNJ nº 65/2008** para validação imediata de dígitos verificadores antes de qualquer chamada externa.
2. **Resiliência Corporativa:** Chamadas HTTP protegidas por políticas do **Polly** com *exponential backoff* e mitigação de falhas transitórias (429, 502, 503, timeouts).
3. **Persistência Flexível:** Repositório NoSQL em **MongoDB** para suportar a diversidade de metadados dos diferentes ramos da justiça, com fallback *in-memory* transparente para desenvolvimento local.
4. **Interface Reativa & UX Moderna:** Single Page Application em **Angular 21** com máscara dinâmica de entrada, validação em tempo real e visualização em linha do tempo (*Timeline UI*).

---

## 🏛️ Arquitetura da Solução

O backend segue os preceitos de **Clean Architecture**, isolando as regras de negócio de detalhes de infraestrutura:

```
legal-process-monitor/
├── src/
│   ├── LegalProcessMonitor.Domain/          # Regras puras, Value Objects (CNJ Mod 97), Entidades
│   ├── LegalProcessMonitor.Infrastructure/  # Conector DataJud (Polly), MongoDB Repository
│   └── LegalProcessMonitor.Api/             # ASP.NET Core Web API, Controllers REST
├── tests/
│   └── LegalProcessMonitor.Tests/           # Testes unitários TDD (xUnit + FluentAssertions)
└── frontend/                                # SPA em Angular 21 (Signals, RxJS, SCSS)
```

### Fluxo de Dados

```
[ Usuário / Frontend Angular ]
              │ (GET /api/processos/{numeroProcesso})
              ▼
   [ ASP.NET Core Web API ]
              │
              ├──> [ Validação CNJ Módulo 97 (Domain) ]
              │
              ├──> [ MongoDB (Cache & Histórico) ] ── (Se já existir, retorna)
              │
              └──> [ Conector DataJud CNJ ] ────── (Com Polly Retry/Backoff)
                            │
                            ▼
              [ API Pública do CNJ ]
```

---

## 🚀 Como Executar Localmente

### Pré-requisitos
* [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
* [Node.js (v20+)](https://nodejs.org/)
* *(Opcional)* MongoDB rodando localmente na porta 27017 (o sistema detecta automaticamente e utiliza repositório em memória caso o MongoDB esteja offline).

### 1. Executando o Backend (.NET 9)
```bash
cd src/LegalProcessMonitor.Api
dotnet run
```
A API iniciará por padrão em `http://localhost:5000` (ou porta configurada no `launchSettings.json`).

### 2. Executando os Testes Unitários
```bash
dotnet test
```

### 3. Executando o Frontend (Angular 21)
```bash
cd frontend
npm install
npm start
```
Acesse a aplicação no seu navegador em: `http://localhost:4200`.

---

## 🧪 Casos de Teste Válidos no Padrão CNJ

Você pode testar a consulta com números no formato oficial:
* **TJSP (Processo Real Ativo):** `2101760-84.2025.8.26.0000`
* **TRF3 (Justiça Federal de SP/MS):** `5000001-80.2023.4.03.6100`

---

## 📄 Licença
Distribuído sob a licença MIT. Criado para fins demonstrativos e portfólio de engenharia de software de alta performance.
