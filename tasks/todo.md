# Tarefas do Projeto - LegalProcessMonitor

- [x] **Fase 1: Setup da Solução e Arquitetura Base**
  - [x] Criar solução .NET 9 Clean Architecture (`Domain`, `Infrastructure`, `Api`, `Tests`)
  - [x] Instalar pacotes essenciais (`MongoDB.Driver`, `Polly`, `FluentAssertions`, `Moq`)
  - [x] Validar compilação inicial da solução

- [x] **Fase 2: Domínio & Validação Matemática do CNJ (TDD)**
  - [x] Implementar Value Object `NumeroProcessoCnj` com cálculo de dígito verificador módulo 97 (Resolução CNJ 65/2008)
  - [x] Implementar entidades do domínio: `Processo`, `Movimentacao`, `Envolvido`
  - [x] Definir contratos de interfaces: `ITribunalConnector`, `IProcessoRepository`
  - [x] Escrever suite de testes unitários com xUnit e FluentAssertions

- [x] **Fase 3: Infraestrutura (Conector DataJud CNJ & MongoDB)**
  - [x] Implementar cliente HTTP resiliente para o DataJud (CNJ) com políticas Polly (retry + backoff)
  - [x] Implementar repositório MongoDB com fallback In-Memory e tratamento de idempotência
  - [x] Validar injeção de dependências e resiliência

- [x] **Fase 4: API REST ASP.NET Core**
  - [x] Configurar DI, logging, CORS para Angular e OpenAPI
  - [x] Implementar `ProcessosController` com endpoints de consulta por CNJ e listagem
  - [x] Validar compilação e execução da API

- [x] **Fase 5: Frontend Angular**
  - [x] Inicializar projeto Angular na pasta `frontend/`
  - [x] Implementar tela de busca com máscara CNJ e validação em tempo real
  - [x] Implementar componente de Linha do Tempo (Timeline) para renderizar os andamentos
  - [x] Validar compilação de produção (`ng build`) com sucesso

- [x] **Fase 6: Crawler e-SAJ para Documentos e Decisões Públicas**
  - [x] Implementar `ITribunalDocumentScraper` e `EsajDocumentScraper` (TJSP 2º Grau)
  - [x] Extrair acórdãos, decisões de mérito, situação do julgamento e desembargador relator
  - [x] Endpoint REST: `GET /api/processos/{numeroProcesso}/documentos`
  - [x] Interface Angular atualizada com card visual de decisões públicas capturadas
