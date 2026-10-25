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

        return new EstadoWorkspaceTenant(
            TenantId: tenant,
            Configuracao: config,
            Watchlist: watchlist,
            NotasJuizo: notas,
            SuspensoesLocais: suspensoes,
            TarefasKanban: tarefas,
            ItensRadar: radar);
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
}
