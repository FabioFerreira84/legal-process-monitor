using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;
using Microsoft.Data.Sqlite;

namespace LegalProcessMonitor.Infrastructure.Repositories;

public sealed class SqliteWorkspaceRepository : IWorkspaceRepository
{
    private readonly string _connectionString;

    public SqliteWorkspaceRepository(string connectionString, bool semearDemoPadrao = false)
    {
        _connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? "Data Source=legalmonitor.db"
            : connectionString;

        InicializarBanco();

        if (semearDemoPadrao)
        {
            SemearWorkspaceDemonstracaoSeVazio();
        }
    }

    private void InicializarBanco()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;

            CREATE TABLE IF NOT EXISTS workspace_config (
                tenant_id TEXT PRIMARY KEY,
                nome_escritorio TEXT NOT NULL,
                inscricoes_oab_json TEXT NOT NULL,
                webhook_url TEXT NULL,
                telegram_bot_token TEXT NULL,
                telegram_chat_id TEXT NULL,
                ativo INTEGER NOT NULL DEFAULT 1,
                ultima_sincronizacao_utc TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS workspace_watchlist (
                tenant_id TEXT NOT NULL,
                numero_processo TEXT NOT NULL,
                classe TEXT NOT NULL,
                orgao_julgador TEXT NOT NULL,
                tribunal TEXT NOT NULL,
                total_movimentacoes INTEGER NOT NULL,
                adicionado_em_utc TEXT NOT NULL,
                tem_novidade INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (tenant_id, numero_processo)
            );

            CREATE TABLE IF NOT EXISTS workspace_notas_juizo (
                tenant_id TEXT NOT NULL,
                chave_unidade TEXT NOT NULL,
                conteudo_nota TEXT NOT NULL,
                atualizado_em_utc TEXT NOT NULL,
                PRIMARY KEY (tenant_id, chave_unidade)
            );

            CREATE TABLE IF NOT EXISTS workspace_suspensoes_locais (
                id TEXT NOT NULL,
                tenant_id TEXT NOT NULL,
                data_iso TEXT NOT NULL,
                descricao TEXT NOT NULL,
                comarca_tribunal TEXT NOT NULL,
                criado_em_utc TEXT NOT NULL,
                PRIMARY KEY (tenant_id, id)
            );

            CREATE TABLE IF NOT EXISTS workspace_tarefas_kanban (
                id TEXT NOT NULL,
                tenant_id TEXT NOT NULL,
                numero_processo TEXT NOT NULL,
                titulo TEXT NOT NULL,
                descricao TEXT NOT NULL,
                responsavel TEXT NOT NULL,
                coluna INTEGER NOT NULL,
                prioridade INTEGER NOT NULL,
                prazo_fatal_iso TEXT NOT NULL,
                dias_prazo INTEGER NOT NULL,
                prazo_em_dobro INTEGER NOT NULL DEFAULT 0,
                numero_protocolo TEXT NULL,
                criado_em_utc TEXT NOT NULL,
                atualizado_em_utc TEXT NOT NULL,
                PRIMARY KEY (tenant_id, id)
            );

            CREATE TABLE IF NOT EXISTS workspace_radar_djen (
                id TEXT NOT NULL,
                tenant_id TEXT NOT NULL,
                oab_origem TEXT NOT NULL,
                numero_processo TEXT NOT NULL,
                tribunal TEXT NOT NULL,
                orgao_julgador TEXT NOT NULL,
                data_disponibilizacao_utc TEXT NOT NULL,
                tipo_comunicacao TEXT NOT NULL,
                teor_resumo TEXT NOT NULL,
                link_inteiro_teor TEXT NOT NULL,
                lida INTEGER NOT NULL DEFAULT 0,
                convertida_em_tarefa INTEGER NOT NULL DEFAULT 0,
                capturado_em_utc TEXT NOT NULL,
                PRIMARY KEY (tenant_id, id)
            );

            CREATE TABLE IF NOT EXISTS workspace_teses_juridicas (
                id TEXT NOT NULL,
                tenant_id TEXT NOT NULL,
                titulo TEXT NOT NULL,
                area_direito TEXT NOT NULL,
                eventos_gatilho_json TEXT NOT NULL,
                fundamentacao_legal TEXT NOT NULL,
                precedente_chave TEXT NOT NULL,
                modelo_argumentacao TEXT NOT NULL,
                total_usos INTEGER NOT NULL DEFAULT 0,
                ultimo_processo_aplicado TEXT NULL,
                atualizado_em_utc TEXT NOT NULL,
                PRIMARY KEY (tenant_id, id)
            );

            CREATE TABLE IF NOT EXISTS workspace_teses_aplicadas (
                id TEXT NOT NULL,
                tenant_id TEXT NOT NULL,
                tese_id TEXT NOT NULL,
                titulo_tese TEXT NOT NULL,
                numero_processo TEXT NOT NULL,
                evento_contexto TEXT NOT NULL,
                resultado_esperado_obtido TEXT NOT NULL,
                responsavel TEXT NOT NULL,
                aplicada_em_utc TEXT NOT NULL,
                PRIMARY KEY (tenant_id, id)
            );
        ";
        cmd.ExecuteNonQuery();
    }

    private void SemearWorkspaceDemonstracaoSeVazio()
    {
        const string tenantPadrao = "escritorio-ferreira";
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(1) FROM workspace_config WHERE tenant_id = $tenantId";
        checkCmd.Parameters.AddWithValue("$tenantId", tenantPadrao);
        var existe = Convert.ToInt32(checkCmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        if (existe) return;

        SalvarConfigRadarAsync(new ConfigRadarOab(
            TenantId: tenantPadrao,
            NomeEscritorio: "Ferreira & Associados Advocacia Empresarial",
            InscricoesOab: new[] { "238470/SP" },
            WebhookUrl: null,
            TelegramBotToken: null,
            TelegramChatId: null,
            Ativo: true,
            UltimaSincronizacaoUtc: DateTime.UtcNow)).GetAwaiter().GetResult();

        SalvarItemWatchlistAsync(new ItemWatchlistTenant(
            TenantId: tenantPadrao,
            NumeroProcesso: "2101760-84.2025.8.26.0000",
            Classe: "Agravo de Instrumento",
            OrgaoJulgador: "24ª Câmara de Direito Privado",
            Tribunal: "TJSP",
            TotalMovimentacoesConhecidas: 28,
            AdicionadoEmUtc: DateTime.UtcNow,
            TemNovidade: true)).GetAwaiter().GetResult();

        SalvarTarefaAsync(new TarefaProcessual(
            Id: "kanban-stj-aresp-3319477",
            TenantId: tenantPadrao,
            NumeroProcesso: "2101760-84.2025.8.26.0000",
            Titulo: "Acompanhar distribuição e contrarrazões no AREsp 3319477/SP (STJ)",
            Descricao: "Disponibilizado no DJEN em 23/10/2026: distribuído ao Min. Antonio Carlos Ferreira (Presidência STJ).",
            Responsavel: "Dr. Fabio Ferreira (OAB/SP 238.470)",
            Coluna: StatusTarefaColuna.Intimacao,
            Prioridade: PrioridadeTarefa.Critica,
            PrazoFatal: new DateOnly(2026, 11, 18),
            DiasPrazo: 15,
            PrazoEmDobro: false,
            NumeroProtocolo: null,
            CriadoEmUtc: DateTime.UtcNow,
            AtualizadoEmUtc: DateTime.UtcNow)).GetAwaiter().GetResult();

        SalvarItensRadarLoteAsync(tenantPadrao, new[]
        {
            new ItemRadarDjen(
                Id: "djen-765092040",
                TenantId: tenantPadrao,
                OabOrigem: "238470/SP",
                NumeroProcesso: "2101760-84.2025.8.26.0000",
                Tribunal: "STJ",
                OrgaoJulgador: "Presidência do STJ",
                DataDisponibilizacao: new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc),
                TipoComunicacao: "Intimação de Distribuição (AREsp 3319477/SP)",
                TeorResumo: "PROCESSO DISTRIBUÍDO no STJ (AREsp 3319477/SP - Registro 2026/0380506-6) em 22/10/2026 ao Min. Presidente Antonio Carlos Ferreira. Agravante: Banco Bradesco S.A. Agravada: Cooperativa Agrícola Mista de Adamantina.",
                LinkInteiroTeor: "https://comunica.pje.jus.br",
                Lida: false,
                ConvertidaEmTarefa: true,
                CapturadoEmUtc: DateTime.UtcNow)
        }).GetAwaiter().GetResult();

        // Semeia o Banco de Teses Jurídicas do Escritório com teses reais de alta eficácia e vínculo real de uso
        SalvarTeseJuridicaAsync(new TeseJuridicaTenant(
            Id: "tese-stj-distinguishing-sumula-7",
            TenantId: tenantPadrao,
            Titulo: "Impugnação ao AREsp — Súmula 7/STJ vs. Revaloração Jurídica de Fatos Incontroversos",
            AreaDireito: "Processo Civil / Tribunais Superiores (STJ)",
            EventosGatilho: new[] { "AREsp", "REsp", "STJ", "Agravo em Recurso Especial", "Contrarrazões", "Distribuição", "Súmula 7" },
            FundamentacaoLegal: "Art. 1.029 e Art. 1.030, V, do CPC/2015; Súmula 7/STJ; Súmula 182/STJ",
            PrecedenteChave: "STJ — AgInt no AREsp 3319477/SP e Jurisprudência da Corte Especial (Súmula 182/STJ por ausência de impugnação específica aos fundamentos da decisão de inadmissibilidade)",
            ModeloArgumentacao: "1. Preliminar de Não Conhecimento (Súmula 182/STJ): a agravante limitou-se a repetir as razões do Recurso Especial sem infirmar especificamente todos os capítulos da decisão da Presidência de origem.\n2. Óbice da Súmula 7/STJ: a reversão do acórdão recorrido demandaria o reexame fático-probatório e a interpretação de cláusulas contratuais (Súmula 5/STJ), vedados na via estreita do apelo nobre.",
            TotalUsos: 4,
            UltimoProcessoAplicado: "2101760-84.2025.8.26.0000",
            AtualizadoEmUtc: DateTime.UtcNow)).GetAwaiter().GetResult();

        SalvarTeseJuridicaAsync(new TeseJuridicaTenant(
            Id: "tese-possessoria-requisitos-561-cpc",
            TenantId: tenantPadrao,
            Titulo: "Tutela Possessória de Urgência — Requisitos Cumulativos do Art. 561 do CPC e Periculum in Mora Inverso",
            AreaDireito: "Direito Civil / Possessórias e Agronegócio",
            EventosGatilho: new[] { "Esbulho", "Turbação", "Ameaça", "Agravo de Instrumento", "Reintegração de Posse", "Liminar", "Art. 561" },
            FundamentacaoLegal: "Arts. 300, 560, 561 e 562 do CPC/2015; Art. 1.210 do Código Civil",
            PrecedenteChave: "TJSP — 24ª Câmara de Direito Privado (Agravo de Instrumento nº 2101760-84.2025.8.26.0000 — Rel. Des. Pedro Paulo Maillet Preuss)",
            ModeloArgumentacao: "A concessão ou manutenção de liminar possessória inaudita altera exige prova documental inequívoca da posse anterior, do esbulho ou turbação praticado há menos de ano e dia (força nova) e da perda ou receio justo da posse (Art. 561, I a IV, do CPC), sob pena de irreversibilidade prática no ciclo produtivo agrícola (Art. 300, § 3º, do CPC).",
            TotalUsos: 7,
            UltimoProcessoAplicado: "2101760-84.2025.8.26.0000",
            AtualizadoEmUtc: DateTime.UtcNow)).GetAwaiter().GetResult();

        SalvarTeseJuridicaAsync(new TeseJuridicaTenant(
            Id: "tese-ms-tributario-prova-pre-constituida",
            TenantId: tenantPadrao,
            Titulo: "Mandado de Segurança Tributário — Direito Líquido e Certo, Declaração de Compensação (Tema 118/STJ) e Art. 485, IV do CPC",
            AreaDireito: "Direito Tributário / Processo Constitucional",
            EventosGatilho: new[] { "Mandado de Segurança", "Tributário", "Receita Federal", "TRF3", "Ausência das condições da ação", "Art. 485", "Prova pré-constituída" },
            FundamentacaoLegal: "Art. 5º, LXIX, da CF/88; Art. 1º e Art. 6º, § 5º, da Lei nº 12.016/2009; Súmula 213/STJ; Tema Repetitivo 118/STJ",
            PrecedenteChave: "STJ — Tema Repetitivo 118 (REsp 1.111.164/BA): Suficiência de documentos comprobatórios da condição de credor tributário para declaração do direito à compensação, reservando-se a apuração de valores para a fase administrativa/liquidação",
            ModeloArgumentacao: "Nos termos da Súmula 213/STJ e do Tema Repetitivo 118/STJ, quando o pedido em Mandado de Segurança visa o reconhecimento da matriz jurídica do indébito ou do direito à compensação tributária, mostra-se desnecessária dilação probatória contábil exauriente na fase cognitiva, bastando a comprovação documental da sujeição passiva tributária.",
            TotalUsos: 3,
            UltimoProcessoAplicado: "5000001-05.2023.4.03.6100",
            AtualizadoEmUtc: DateTime.UtcNow)).GetAwaiter().GetResult();

        SalvarTeseJuridicaAsync(new TeseJuridicaTenant(
            Id: "tese-execucao-sisbajud-menor-onerosidade",
            TenantId: tenantPadrao,
            Titulo: "Desbloqueio de Ativos SISBAJUD — Impenhorabilidade de Capital de Giro Essencial e Menor Onerosidade (Art. 805 CPC)",
            AreaDireito: "Execução Civil / Empresarial",
            EventosGatilho: new[] { "SISBAJUD", "Penhora", "Bloqueio", "Execução", "Cumprimento de Sentença", "Art. 805", "Art. 833" },
            FundamentacaoLegal: "Art. 805, parágrafo único, Art. 833, IV e X, e Art. 854, § 3º, do CPC/2015",
            PrecedenteChave: "STJ — Corte Especial (EREsp 1.874.222/DF) e 3ª Turma: Mitigação e proteção do capital de giro indispensável à continuidade da atividade empresarial comprovada documentalmente em 5 dias úteis (Art. 854, § 3º, CPC)",
            ModeloArgumentacao: "Requer-se a liberação imediata do excesso de constrição via SISBAJUD (Art. 854, § 3º, II, do CPC) ou a substituição da penhora em dinheiro por garantia idônea (fiança bancária/seguro garantia acrescido de 30% — Art. 835, § 2º, do CPC), demonstrando-se que o bloqueio de fluxo de caixa operacional inviabiliza a folha salarial e a atividade-fim da executada.",
            TotalUsos: 5,
            UltimoProcessoAplicado: "1006805-85.2025.8.26.0224",
            AtualizadoEmUtc: DateTime.UtcNow)).GetAwaiter().GetResult();

        RegistrarAplicacaoTeseAsync(new AplicacaoTeseProcesso(
            Id: "aplic-demo-001",
            TenantId: tenantPadrao,
            TeseId: "tese-possessoria-requisitos-561-cpc",
            TituloTese: "Tutela Possessória de Urgência — Requisitos Cumulativos do Art. 561 do CPC e Periculum in Mora Inverso",
            NumeroProcesso: "2101760-84.2025.8.26.0000",
            EventoContexto: "Julgamento Colegiado na 24ª Câmara de Direito Privado do TJSP",
            ResultadoEsperadoOuObtido: "Tese acolhida no Acórdão de 2º Grau — Recurso da parte adversa improvido por unanimidade",
            Responsavel: "Dr. Fabio Ferreira (OAB/SP 238.470)",
            AplicadaEmUtc: new DateTime(2025, 10, 15, 14, 0, 0, DateTimeKind.Utc))).GetAwaiter().GetResult();
    }

    public async Task<EstadoWorkspaceTenant> ObterEstadoWorkspaceAsync(string tenantId, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tenantId);
        var config = await ObterConfigRadarInternoAsync(tenant, ct);
        var watchlist = await ListarWatchlistAsync(tenant, ct);
        var notas = await ListarNotasJuizoAsync(tenant, ct);
        var suspensoes = await ListarSuspensoesLocaisAsync(tenant, ct);
        var tarefas = await ListarTarefasAsync(tenant, ct);
        var radar = await ListarItensRadarAsync(tenant, ct);
        var teses = await ListarTesesJuridicasAsync(tenant, ct);
        var aplicacoes = await ListarAplicacoesTesesAsync(tenant, null, ct);

        return new EstadoWorkspaceTenant(
            TenantId: tenant,
            Configuracao: config,
            Watchlist: watchlist,
            NotasJuizo: notas,
            SuspensoesLocais: suspensoes,
            TarefasKanban: tarefas,
            ItensRadar: radar,
            TesesJuridicas: teses,
            HistoricoAplicacoesTeses: aplicacoes);
    }

    private async Task<ConfigRadarOab> ObterConfigRadarInternoAsync(string tenant, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT nome_escritorio, inscricoes_oab_json, webhook_url, telegram_bot_token, telegram_chat_id, ativo, ultima_sincronizacao_utc
            FROM workspace_config
            WHERE tenant_id = $tenantId";
        cmd.Parameters.AddWithValue("$tenantId", tenant);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var oabsJson = reader.GetString(1);
            var oabs = JsonSerializer.Deserialize<List<string>>(oabsJson) ?? new List<string>();
            DateTime? ultSync = reader.IsDBNull(6)
                ? null
                : DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            return new ConfigRadarOab(
                TenantId: tenant,
                NomeEscritorio: reader.GetString(0),
                InscricoesOab: oabs,
                WebhookUrl: reader.IsDBNull(2) ? null : reader.GetString(2),
                TelegramBotToken: reader.IsDBNull(3) ? null : reader.GetString(3),
                TelegramChatId: reader.IsDBNull(4) ? null : reader.GetString(4),
                Ativo: reader.GetInt32(5) == 1,
                UltimaSincronizacaoUtc: ultSync);
        }

        return new ConfigRadarOab(
            TenantId: tenant,
            NomeEscritorio: $"Workspace {tenant}",
            InscricoesOab: Array.Empty<string>(),
            WebhookUrl: null,
            TelegramBotToken: null,
            TelegramChatId: null,
            Ativo: true,
            UltimaSincronizacaoUtc: null);
    }

    public async Task<ConfigRadarOab> SalvarConfigRadarAsync(ConfigRadarOab config, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(config.TenantId);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workspace_config (tenant_id, nome_escritorio, inscricoes_oab_json, webhook_url, telegram_bot_token, telegram_chat_id, ativo, ultima_sincronizacao_utc)
            VALUES ($tenantId, $nome, $oabs, $webhook, $tgToken, $tgChat, $ativo, $ultSync)
            ON CONFLICT(tenant_id) DO UPDATE SET
                nome_escritorio = excluded.nome_escritorio,
                inscricoes_oab_json = excluded.inscricoes_oab_json,
                webhook_url = excluded.webhook_url,
                telegram_bot_token = excluded.telegram_bot_token,
                telegram_chat_id = excluded.telegram_chat_id,
                ativo = excluded.ativo,
                ultima_sincronizacao_utc = excluded.ultima_sincronizacao_utc;";

        cmd.Parameters.AddWithValue("$tenantId", tenant);
        cmd.Parameters.AddWithValue("$nome", config.NomeEscritorio ?? $"Workspace {tenant}");
        cmd.Parameters.AddWithValue("$oabs", JsonSerializer.Serialize(config.InscricoesOab ?? Array.Empty<string>()));
        cmd.Parameters.AddWithValue("$webhook", (object?)config.WebhookUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tgToken", (object?)config.TelegramBotToken ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tgChat", (object?)config.TelegramChatId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ativo", config.Ativo ? 1 : 0);
        cmd.Parameters.AddWithValue("$ultSync", config.UltimaSincronizacaoUtc.HasValue
            ? config.UltimaSincronizacaoUtc.Value.ToString("O", CultureInfo.InvariantCulture)
            : DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
        return config with { TenantId = tenant };
    }

    public async Task<ItemWatchlistTenant> SalvarItemWatchlistAsync(ItemWatchlistTenant item, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(item.TenantId);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workspace_watchlist (tenant_id, numero_processo, classe, orgao_julgador, tribunal, total_movimentacoes, adicionado_em_utc, tem_novidade)
            VALUES ($tenantId, $numero, $classe, $orgao, $tribunal, $totalMovs, $adicionadoEm, $temNovidade)
            ON CONFLICT(tenant_id, numero_processo) DO UPDATE SET
                classe = excluded.classe,
                orgao_julgador = excluded.orgao_julgador,
                tribunal = excluded.tribunal,
                total_movimentacoes = excluded.total_movimentacoes,
                tem_novidade = excluded.tem_novidade;";

        cmd.Parameters.AddWithValue("$tenantId", tenant);
        cmd.Parameters.AddWithValue("$numero", item.NumeroProcesso.Trim());
        cmd.Parameters.AddWithValue("$classe", item.Classe ?? string.Empty);
        cmd.Parameters.AddWithValue("$orgao", item.OrgaoJulgador ?? string.Empty);
        cmd.Parameters.AddWithValue("$tribunal", item.Tribunal ?? "TJSP");
        cmd.Parameters.AddWithValue("$totalMovs", item.TotalMovimentacoesConhecidas);
        cmd.Parameters.AddWithValue("$adicionadoEm", item.AdicionadoEmUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$temNovidade", item.TemNovidade ? 1 : 0);

        await cmd.ExecuteNonQueryAsync(ct);
        return item with { TenantId = tenant };
    }

    public async Task<bool> RemoverItemWatchlistAsync(string tenantId, string numeroProcesso, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tenantId);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM workspace_watchlist WHERE tenant_id = $tenantId AND numero_processo = $numero";
        cmd.Parameters.AddWithValue("$tenantId", tenant);
        cmd.Parameters.AddWithValue("$numero", numeroProcesso.Trim());
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<IReadOnlyList<ItemWatchlistTenant>> ListarWatchlistAsync(string tenantId, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tenantId);
        var lista = new List<ItemWatchlistTenant>();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT numero_processo, classe, orgao_julgador, tribunal, total_movimentacoes, adicionado_em_utc, tem_novidade
            FROM workspace_watchlist
            WHERE tenant_id = $tenantId
            ORDER BY adicionado_em_utc DESC";
        cmd.Parameters.AddWithValue("$tenantId", tenant);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lista.Add(new ItemWatchlistTenant(
                TenantId: tenant,
                NumeroProcesso: reader.GetString(0),
                Classe: reader.GetString(1),
                OrgaoJulgador: reader.GetString(2),
                Tribunal: reader.GetString(3),
                TotalMovimentacoesConhecidas: reader.GetInt32(4),
                AdicionadoEmUtc: DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                TemNovidade: reader.GetInt32(6) == 1));
        }

        return lista;
    }

    public async Task<NotaJuizoTenant> SalvarNotaJuizoAsync(NotaJuizoTenant nota, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(nota.TenantId);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workspace_notas_juizo (tenant_id, chave_unidade, conteudo_nota, atualizado_em_utc)
            VALUES ($tenantId, $chave, $conteudo, $atualizadoEm)
            ON CONFLICT(tenant_id, chave_unidade) DO UPDATE SET
                conteudo_nota = excluded.conteudo_nota,
                atualizado_em_utc = excluded.atualizado_em_utc;";

        cmd.Parameters.AddWithValue("$tenantId", tenant);
        cmd.Parameters.AddWithValue("$chave", nota.ChaveUnidade.Trim());
        cmd.Parameters.AddWithValue("$conteudo", nota.ConteudoNota ?? string.Empty);
        cmd.Parameters.AddWithValue("$atualizadoEm", nota.AtualizadoEmUtc.ToString("O", CultureInfo.InvariantCulture));

        await cmd.ExecuteNonQueryAsync(ct);
        return nota with { TenantId = tenant };
    }

    public async Task<IReadOnlyDictionary<string, string>> ListarNotasJuizoAsync(string tenantId, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tenantId);
        var dict = new Dictionary<string, string>();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT chave_unidade, conteudo_nota FROM workspace_notas_juizo WHERE tenant_id = $tenantId";
        cmd.Parameters.AddWithValue("$tenantId", tenant);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            dict[reader.GetString(0)] = reader.GetString(1);
        }

        return dict;
    }

    public async Task<SuspensaoForenseLocal> AdicionarSuspensaoLocalAsync(SuspensaoForenseLocal suspensao, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(suspensao.TenantId);
        var id = string.IsNullOrWhiteSpace(suspensao.Id) ? Guid.NewGuid().ToString("N") : suspensao.Id.Trim();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workspace_suspensoes_locais (id, tenant_id, data_iso, descricao, comarca_tribunal, criado_em_utc)
            VALUES ($id, $tenantId, $dataIso, $descricao, $comarca, $criadoEm)
            ON CONFLICT(tenant_id, id) DO UPDATE SET
                data_iso = excluded.data_iso,
                descricao = excluded.descricao,
                comarca_tribunal = excluded.comarca_tribunal;";

        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$tenantId", tenant);
        cmd.Parameters.AddWithValue("$dataIso", suspensao.Data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$descricao", suspensao.Descricao.Trim());
        cmd.Parameters.AddWithValue("$comarca", string.IsNullOrWhiteSpace(suspensao.ComarcaOuTribunal) ? "TJSP" : suspensao.ComarcaOuTribunal.Trim());
        cmd.Parameters.AddWithValue("$criadoEm", suspensao.CriadoEmUtc.ToString("O", CultureInfo.InvariantCulture));

        await cmd.ExecuteNonQueryAsync(ct);
        return suspensao with { Id = id, TenantId = tenant };
    }

    public async Task<bool> RemoverSuspensaoLocalAsync(string tenantId, string suspensaoId, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tenantId);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM workspace_suspensoes_locais WHERE tenant_id = $tenantId AND id = $id";
        cmd.Parameters.AddWithValue("$tenantId", tenant);
        cmd.Parameters.AddWithValue("$id", suspensaoId.Trim());
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<IReadOnlyList<SuspensaoForenseLocal>> ListarSuspensoesLocaisAsync(string tenantId, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tenantId);
        var lista = new List<SuspensaoForenseLocal>();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, data_iso, descricao, comarca_tribunal, criado_em_utc
            FROM workspace_suspensoes_locais
            WHERE tenant_id = $tenantId
            ORDER BY data_iso ASC";
        cmd.Parameters.AddWithValue("$tenantId", tenant);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lista.Add(new SuspensaoForenseLocal(
                Id: reader.GetString(0),
                TenantId: tenant,
                Data: DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Descricao: reader.GetString(2),
                ComarcaOuTribunal: reader.GetString(3),
                CriadoEmUtc: DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return lista;
    }

    public async Task<TarefaProcessual> SalvarTarefaAsync(TarefaProcessual tarefa, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tarefa.TenantId);
        var id = string.IsNullOrWhiteSpace(tarefa.Id) ? Guid.NewGuid().ToString("N") : tarefa.Id.Trim();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workspace_tarefas_kanban (
                id, tenant_id, numero_processo, titulo, descricao, responsavel,
                coluna, prioridade, prazo_fatal_iso, dias_prazo, prazo_em_dobro,
                numero_protocolo, criado_em_utc, atualizado_em_utc
            )
            VALUES (
                $id, $tenantId, $numero, $titulo, $descricao, $responsavel,
                $coluna, $prioridade, $prazoFatal, $diasPrazo, $dobro,
                $protocolo, $criadoEm, $atualizadoEm
            )
            ON CONFLICT(tenant_id, id) DO UPDATE SET
                numero_processo = excluded.numero_processo,
                titulo = excluded.titulo,
                descricao = excluded.descricao,
                responsavel = excluded.responsavel,
                coluna = excluded.coluna,
                prioridade = excluded.prioridade,
                prazo_fatal_iso = excluded.prazo_fatal_iso,
                dias_prazo = excluded.dias_prazo,
                prazo_em_dobro = excluded.prazo_em_dobro,
                numero_protocolo = excluded.numero_protocolo,
                atualizado_em_utc = excluded.atualizado_em_utc;";

        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$tenantId", tenant);
        cmd.Parameters.AddWithValue("$numero", tarefa.NumeroProcesso.Trim());
        cmd.Parameters.AddWithValue("$titulo", tarefa.Titulo.Trim());
        cmd.Parameters.AddWithValue("$descricao", tarefa.Descricao ?? string.Empty);
        cmd.Parameters.AddWithValue("$responsavel", string.IsNullOrWhiteSpace(tarefa.Responsavel) ? "Advogado Responsável" : tarefa.Responsavel.Trim());
        cmd.Parameters.AddWithValue("$coluna", (int)tarefa.Coluna);
        cmd.Parameters.AddWithValue("$prioridade", (int)tarefa.Prioridade);
        cmd.Parameters.AddWithValue("$prazoFatal", tarefa.PrazoFatal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$diasPrazo", tarefa.DiasPrazo);
        cmd.Parameters.AddWithValue("$dobro", tarefa.PrazoEmDobro ? 1 : 0);
        cmd.Parameters.AddWithValue("$protocolo", (object?)tarefa.NumeroProtocolo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$criadoEm", tarefa.CriadoEmUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$atualizadoEm", tarefa.AtualizadoEmUtc.ToString("O", CultureInfo.InvariantCulture));

        await cmd.ExecuteNonQueryAsync(ct);
        return tarefa with { Id = id, TenantId = tenant };
    }

    public async Task<bool> RemoverTarefaAsync(string tenantId, string tarefaId, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tenantId);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM workspace_tarefas_kanban WHERE tenant_id = $tenantId AND id = $id";
        cmd.Parameters.AddWithValue("$tenantId", tenant);
        cmd.Parameters.AddWithValue("$id", tarefaId.Trim());
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<IReadOnlyList<TarefaProcessual>> ListarTarefasAsync(string tenantId, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tenantId);
        var lista = new List<TarefaProcessual>();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, numero_processo, titulo, descricao, responsavel,
                   coluna, prioridade, prazo_fatal_iso, dias_prazo, prazo_em_dobro,
                   numero_protocolo, criado_em_utc, atualizado_em_utc
            FROM workspace_tarefas_kanban
            WHERE tenant_id = $tenantId
            ORDER BY prazo_fatal_iso ASC, prioridade DESC";
        cmd.Parameters.AddWithValue("$tenantId", tenant);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lista.Add(new TarefaProcessual(
                Id: reader.GetString(0),
                TenantId: tenant,
                NumeroProcesso: reader.GetString(1),
                Titulo: reader.GetString(2),
                Descricao: reader.GetString(3),
                Responsavel: reader.GetString(4),
                Coluna: (StatusTarefaColuna)reader.GetInt32(5),
                Prioridade: (PrioridadeTarefa)reader.GetInt32(6),
                PrazoFatal: DateOnly.ParseExact(reader.GetString(7), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DiasPrazo: reader.GetInt32(8),
                PrazoEmDobro: reader.GetInt32(9) == 1,
                NumeroProtocolo: reader.IsDBNull(10) ? null : reader.GetString(10),
                CriadoEmUtc: DateTime.Parse(reader.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                AtualizadoEmUtc: DateTime.Parse(reader.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return lista;
    }

    public async Task<int> SalvarItensRadarLoteAsync(string tenantId, IEnumerable<ItemRadarDjen> itens, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tenantId);
        var inseridos = 0;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        foreach (var item in itens)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO workspace_radar_djen (
                    id, tenant_id, oab_origem, numero_processo, tribunal, orgao_julgador,
                    data_disponibilizacao_utc, tipo_comunicacao, teor_resumo, link_inteiro_teor,
                    lida, convertida_em_tarefa, capturado_em_utc
                )
                VALUES (
                    $id, $tenantId, $oab, $numero, $tribunal, $orgao,
                    $dataDisp, $tipo, $teor, $link,
                    $lida, $convertida, $capturadoEm
                );";

            cmd.Parameters.AddWithValue("$id", item.Id.Trim());
            cmd.Parameters.AddWithValue("$tenantId", tenant);
            cmd.Parameters.AddWithValue("$oab", item.OabOrigem ?? string.Empty);
            cmd.Parameters.AddWithValue("$numero", item.NumeroProcesso ?? string.Empty);
            cmd.Parameters.AddWithValue("$tribunal", item.Tribunal ?? "TJSP");
            cmd.Parameters.AddWithValue("$orgao", item.OrgaoJulgador ?? string.Empty);
            cmd.Parameters.AddWithValue("$dataDisp", item.DataDisponibilizacao.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$tipo", item.TipoComunicacao ?? "Intimação");
            cmd.Parameters.AddWithValue("$teor", item.TeorResumo ?? string.Empty);
            cmd.Parameters.AddWithValue("$link", item.LinkInteiroTeor ?? "https://comunica.pje.jus.br");
            cmd.Parameters.AddWithValue("$lida", item.Lida ? 1 : 0);
            cmd.Parameters.AddWithValue("$convertida", item.ConvertidaEmTarefa ? 1 : 0);
            cmd.Parameters.AddWithValue("$capturadoEm", item.CapturadoEmUtc.ToString("O", CultureInfo.InvariantCulture));

            var rows = await cmd.ExecuteNonQueryAsync(ct);
            if (rows > 0) inseridos++;
        }

        return inseridos;
    }

    public async Task<bool> MarcarItemRadarLidoAsync(string tenantId, string itemId, bool convertidaEmTarefa = false, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tenantId);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE workspace_radar_djen
            SET lida = 1,
                convertida_em_tarefa = CASE WHEN $conv = 1 THEN 1 ELSE convertida_em_tarefa END
            WHERE tenant_id = $tenantId AND id = $id";
        cmd.Parameters.AddWithValue("$tenantId", tenant);
        cmd.Parameters.AddWithValue("$id", itemId.Trim());
        cmd.Parameters.AddWithValue("$conv", convertidaEmTarefa ? 1 : 0);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<IReadOnlyList<ItemRadarDjen>> ListarItensRadarAsync(string tenantId, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tenantId);
        var lista = new List<ItemRadarDjen>();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, oab_origem, numero_processo, tribunal, orgao_julgador,
                   data_disponibilizacao_utc, tipo_comunicacao, teor_resumo, link_inteiro_teor,
                   lida, convertida_em_tarefa, capturado_em_utc
            FROM workspace_radar_djen
            WHERE tenant_id = $tenantId
            ORDER BY data_disponibilizacao_utc DESC";
        cmd.Parameters.AddWithValue("$tenantId", tenant);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lista.Add(new ItemRadarDjen(
                Id: reader.GetString(0),
                TenantId: tenant,
                OabOrigem: reader.GetString(1),
                NumeroProcesso: reader.GetString(2),
                Tribunal: reader.GetString(3),
                OrgaoJulgador: reader.GetString(4),
                DataDisponibilizacao: DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                TipoComunicacao: reader.GetString(6),
                TeorResumo: reader.GetString(7),
                LinkInteiroTeor: reader.GetString(8),
                Lida: reader.GetInt32(9) == 1,
                ConvertidaEmTarefa: reader.GetInt32(10) == 1,
                CapturadoEmUtc: DateTime.Parse(reader.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return lista;
    }

    public async Task<TeseJuridicaTenant> SalvarTeseJuridicaAsync(TeseJuridicaTenant tese, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tese.TenantId);
        var id = string.IsNullOrWhiteSpace(tese.Id) ? $"tese-{Guid.NewGuid():N}" : tese.Id.Trim();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workspace_teses_juridicas (
                id, tenant_id, titulo, area_direito, eventos_gatilho_json,
                fundamentacao_legal, precedente_chave, modelo_argumentacao,
                total_usos, ultimo_processo_aplicado, atualizado_em_utc
            )
            VALUES (
                $id, $tenantId, $titulo, $area, $gatilhos,
                $fundamentacao, $precedente, $modelo,
                $totalUsos, $ultimoProc, $atualizadoEm
            )
            ON CONFLICT(tenant_id, id) DO UPDATE SET
                titulo = excluded.titulo,
                area_direito = excluded.area_direito,
                eventos_gatilho_json = excluded.eventos_gatilho_json,
                fundamentacao_legal = excluded.fundamentacao_legal,
                precedente_chave = excluded.precedente_chave,
                modelo_argumentacao = excluded.modelo_argumentacao,
                total_usos = excluded.total_usos,
                ultimo_processo_aplicado = excluded.ultimo_processo_aplicado,
                atualizado_em_utc = excluded.atualizado_em_utc;";

        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$tenantId", tenant);
        cmd.Parameters.AddWithValue("$titulo", tese.Titulo.Trim());
        cmd.Parameters.AddWithValue("$area", string.IsNullOrWhiteSpace(tese.AreaDireito) ? "Contencioso Cível" : tese.AreaDireito.Trim());
        cmd.Parameters.AddWithValue("$gatilhos", JsonSerializer.Serialize(tese.EventosGatilho ?? Array.Empty<string>()));
        cmd.Parameters.AddWithValue("$fundamentacao", tese.FundamentacaoLegal ?? string.Empty);
        cmd.Parameters.AddWithValue("$precedente", tese.PrecedenteChave ?? string.Empty);
        cmd.Parameters.AddWithValue("$modelo", tese.ModeloArgumentacao ?? string.Empty);
        cmd.Parameters.AddWithValue("$totalUsos", Math.Max(0, tese.TotalUsos));
        cmd.Parameters.AddWithValue("$ultimoProc", (object?)tese.UltimoProcessoAplicado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$atualizadoEm", tese.AtualizadoEmUtc.ToString("O", CultureInfo.InvariantCulture));

        await cmd.ExecuteNonQueryAsync(ct);
        return tese with { Id = id, TenantId = tenant };
    }

    public async Task<bool> RemoverTeseJuridicaAsync(string tenantId, string teseId, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tenantId);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM workspace_teses_juridicas WHERE tenant_id = $tenantId AND id = $id";
        cmd.Parameters.AddWithValue("$tenantId", tenant);
        cmd.Parameters.AddWithValue("$id", teseId.Trim());
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<IReadOnlyList<TeseJuridicaTenant>> ListarTesesJuridicasAsync(string tenantId, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tenantId);
        var lista = new List<TeseJuridicaTenant>();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, titulo, area_direito, eventos_gatilho_json,
                   fundamentacao_legal, precedente_chave, modelo_argumentacao,
                   total_usos, ultimo_processo_aplicado, atualizado_em_utc
            FROM workspace_teses_juridicas
            WHERE tenant_id = $tenantId
            ORDER BY total_usos DESC, atualizado_em_utc DESC";
        cmd.Parameters.AddWithValue("$tenantId", tenant);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var gatilhos = JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? new List<string>();
            lista.Add(new TeseJuridicaTenant(
                Id: reader.GetString(0),
                TenantId: tenant,
                Titulo: reader.GetString(1),
                AreaDireito: reader.GetString(2),
                EventosGatilho: gatilhos,
                FundamentacaoLegal: reader.GetString(4),
                PrecedenteChave: reader.GetString(5),
                ModeloArgumentacao: reader.GetString(6),
                TotalUsos: reader.GetInt32(7),
                UltimoProcessoAplicado: reader.IsDBNull(8) ? null : reader.GetString(8),
                AtualizadoEmUtc: DateTime.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return lista;
    }

    public async Task<AplicacaoTeseProcesso> RegistrarAplicacaoTeseAsync(AplicacaoTeseProcesso aplicacao, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(aplicacao.TenantId);
        var id = string.IsNullOrWhiteSpace(aplicacao.Id) ? $"aplic-{Guid.NewGuid():N}" : aplicacao.Id.Trim();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workspace_teses_aplicadas (
                id, tenant_id, tese_id, titulo_tese, numero_processo,
                evento_contexto, resultado_esperado_obtido, responsavel, aplicada_em_utc
            )
            VALUES (
                $id, $tenantId, $teseId, $tituloTese, $numero,
                $contexto, $resultado, $responsavel, $aplicadaEm
            )
            ON CONFLICT(tenant_id, id) DO UPDATE SET
                resultado_esperado_obtido = excluded.resultado_esperado_obtido;

            UPDATE workspace_teses_juridicas
            SET total_usos = total_usos + 1,
                ultimo_processo_aplicado = $numero,
                atualizado_em_utc = $aplicadaEm
            WHERE tenant_id = $tenantId AND id = $teseId;";

        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$tenantId", tenant);
        cmd.Parameters.AddWithValue("$teseId", aplicacao.TeseId.Trim());
        cmd.Parameters.AddWithValue("$tituloTese", aplicacao.TituloTese ?? string.Empty);
        cmd.Parameters.AddWithValue("$numero", aplicacao.NumeroProcesso.Trim());
        cmd.Parameters.AddWithValue("$contexto", aplicacao.EventoContexto ?? string.Empty);
        cmd.Parameters.AddWithValue("$resultado", aplicacao.ResultadoEsperadoOuObtido ?? "Em peticionamento");
        cmd.Parameters.AddWithValue("$responsavel", string.IsNullOrWhiteSpace(aplicacao.Responsavel) ? "Advogado Responsável" : aplicacao.Responsavel.Trim());
        cmd.Parameters.AddWithValue("$aplicadaEm", aplicacao.AplicadaEmUtc.ToString("O", CultureInfo.InvariantCulture));

        await cmd.ExecuteNonQueryAsync(ct);
        return aplicacao with { Id = id, TenantId = tenant };
    }

    public async Task<IReadOnlyList<AplicacaoTeseProcesso>> ListarAplicacoesTesesAsync(string tenantId, string? numeroProcesso = null, CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tenantId);
        var lista = new List<AplicacaoTeseProcesso>();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(numeroProcesso))
        {
            cmd.CommandText = @"
                SELECT id, tese_id, titulo_tese, numero_processo, evento_contexto,
                       resultado_esperado_obtido, responsavel, aplicada_em_utc
                FROM workspace_teses_aplicadas
                WHERE tenant_id = $tenantId
                ORDER BY aplicada_em_utc DESC";
            cmd.Parameters.AddWithValue("$tenantId", tenant);
        }
        else
        {
            cmd.CommandText = @"
                SELECT id, tese_id, titulo_tese, numero_processo, evento_contexto,
                       resultado_esperado_obtido, responsavel, aplicada_em_utc
                FROM workspace_teses_aplicadas
                WHERE tenant_id = $tenantId AND numero_processo = $numero
                ORDER BY aplicada_em_utc DESC";
            cmd.Parameters.AddWithValue("$tenantId", tenant);
            cmd.Parameters.AddWithValue("$numero", numeroProcesso.Trim());
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lista.Add(new AplicacaoTeseProcesso(
                Id: reader.GetString(0),
                TenantId: tenant,
                TeseId: reader.GetString(1),
                TituloTese: reader.GetString(2),
                NumeroProcesso: reader.GetString(3),
                EventoContexto: reader.GetString(4),
                ResultadoEsperadoOuObtido: reader.GetString(5),
                Responsavel: reader.GetString(6),
                AplicadaEmUtc: DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return lista;
    }

    public async Task<EstadoWorkspaceTenant> RestaurarSnapshotWorkspaceAsync(
        string tenantId,
        EstadoWorkspaceTenant snapshot,
        bool mesclarComExistente = true,
        CancellationToken ct = default)
    {
        var tenant = TenantValidador.NormalizarEValidar(tenantId);

        if (!mesclarComExistente)
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM workspace_watchlist WHERE tenant_id = $tenantId;
                DELETE FROM workspace_notas_juizo WHERE tenant_id = $tenantId;
                DELETE FROM workspace_suspensoes_locais WHERE tenant_id = $tenantId;
                DELETE FROM workspace_tarefas_kanban WHERE tenant_id = $tenantId;
                DELETE FROM workspace_radar_djen WHERE tenant_id = $tenantId;
                DELETE FROM workspace_teses_juridicas WHERE tenant_id = $tenantId;
                DELETE FROM workspace_teses_aplicadas WHERE tenant_id = $tenantId;";
            cmd.Parameters.AddWithValue("$tenantId", tenant);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (snapshot.Configuracao is not null)
        {
            await SalvarConfigRadarAsync(snapshot.Configuracao with { TenantId = tenant }, ct);
        }

        if (snapshot.Watchlist is not null)
        {
            foreach (var item in snapshot.Watchlist)
            {
                await SalvarItemWatchlistAsync(item with { TenantId = tenant }, ct);
            }
        }

        if (snapshot.NotasJuizo is not null)
        {
            foreach (var kv in snapshot.NotasJuizo)
            {
                await SalvarNotaJuizoAsync(new NotaJuizoTenant(tenant, kv.Key, kv.Value, DateTime.UtcNow), ct);
            }
        }

        if (snapshot.SuspensoesLocais is not null)
        {
            foreach (var susp in snapshot.SuspensoesLocais)
            {
                await AdicionarSuspensaoLocalAsync(susp with { TenantId = tenant }, ct);
            }
        }

        if (snapshot.TarefasKanban is not null)
        {
            foreach (var tarefa in snapshot.TarefasKanban)
            {
                await SalvarTarefaAsync(tarefa with { TenantId = tenant }, ct);
            }
        }

        if (snapshot.ItensRadar is not null && snapshot.ItensRadar.Count > 0)
        {
            await SalvarItensRadarLoteAsync(tenant, snapshot.ItensRadar.Select(r => r with { TenantId = tenant }), ct);
        }

        if (snapshot.TesesJuridicas is not null)
        {
            foreach (var tese in snapshot.TesesJuridicas)
            {
                await SalvarTeseJuridicaAsync(tese with { TenantId = tenant }, ct);
            }
        }

        if (snapshot.HistoricoAplicacoesTeses is not null)
        {
            foreach (var ap in snapshot.HistoricoAplicacoesTeses)
            {
                await RegistrarAplicacaoTeseAsync(ap with { TenantId = tenant }, ct);
            }
        }

        return await ObterEstadoWorkspaceAsync(tenant, ct);
    }
}
