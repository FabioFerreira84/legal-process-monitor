using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;
using Microsoft.Data.Sqlite;

namespace LegalProcessMonitor.Infrastructure.Repositories;

public sealed class SqliteProcessoRepository : IProcessoRepository
{
    private readonly string _connectionString;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public SqliteProcessoRepository(string connectionString)
    {
        _connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? "Data Source=legalmonitor.db"
            : connectionString;

        InicializarTabelaESemear();
    }

    private void InicializarTabelaESemear()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;

            CREATE TABLE IF NOT EXISTS processos_cache (
                numero_cnj_limpo TEXT PRIMARY KEY,
                numero_cnj_formatado TEXT NOT NULL,
                tribunal TEXT NOT NULL,
                classe TEXT NULL,
                orgao_julgador TEXT NULL,
                ultima_atualizacao_utc TEXT NOT NULL,
                payload_json TEXT NOT NULL
            );
        ";
        cmd.ExecuteNonQuery();

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(1) FROM processos_cache";
        var total = Convert.ToInt32(countCmd.ExecuteScalar(), CultureInfo.InvariantCulture);

        if (total == 0)
        {
            var seedRepo = new InMemoryProcessoRepository();
            var iniciais = seedRepo.ListarRecentesAsync(50).GetAwaiter().GetResult();
            foreach (var p in iniciais)
            {
                SalvarOuAtualizarAsync(p).GetAwaiter().GetResult();
            }
        }
    }

    public async Task<Processo?> ObterPorNumeroAsync(string numeroCnjLimpo, CancellationToken ct = default)
    {
        var limpo = Regex.Replace(numeroCnjLimpo ?? string.Empty, @"\D", "");
        if (string.IsNullOrEmpty(limpo))
            return null;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payload_json FROM processos_cache WHERE numero_cnj_limpo = $limpo";
        cmd.Parameters.AddWithValue("$limpo", limpo);

        var json = await cmd.ExecuteScalarAsync(ct) as string;
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize<Processo>(json, JsonOpts);
    }

    public async Task SalvarOuAtualizarAsync(Processo processo, CancellationToken ct = default)
    {
        var limpo = Regex.Replace(processo.NumeroCnjLimpo ?? processo.NumeroCnjFormatado ?? string.Empty, @"\D", "");
        if (string.IsNullOrEmpty(limpo))
            return;

        processo.NumeroCnjLimpo = limpo;
        var json = JsonSerializer.Serialize(processo, JsonOpts);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO processos_cache (
                numero_cnj_limpo, numero_cnj_formatado, tribunal, classe, orgao_julgador, ultima_atualizacao_utc, payload_json
            )
            VALUES (
                $limpo, $formatado, $tribunal, $classe, $orgao, $ultAtu, $json
            )
            ON CONFLICT(numero_cnj_limpo) DO UPDATE SET
                numero_cnj_formatado = excluded.numero_cnj_formatado,
                tribunal = excluded.tribunal,
                classe = excluded.classe,
                orgao_julgador = excluded.orgao_julgador,
                ultima_atualizacao_utc = excluded.ultima_atualizacao_utc,
                payload_json = excluded.payload_json;";

        cmd.Parameters.AddWithValue("$limpo", limpo);
        cmd.Parameters.AddWithValue("$formatado", processo.NumeroCnjFormatado ?? limpo);
        cmd.Parameters.AddWithValue("$tribunal", processo.Tribunal ?? "TJSP");
        cmd.Parameters.AddWithValue("$classe", (object?)processo.Classe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$orgao", (object?)processo.OrgaoJulgador ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ultAtu", processo.UltimaAtualizacao.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$json", json);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<Processo>> ListarRecentesAsync(int limite = 50, CancellationToken ct = default)
    {
        var lista = new List<Processo>();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT payload_json
            FROM processos_cache
            ORDER BY ultima_atualizacao_utc DESC
            LIMIT $limite";
        cmd.Parameters.AddWithValue("$limite", limite <= 0 ? 50 : limite);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var p = JsonSerializer.Deserialize<Processo>(reader.GetString(0), JsonOpts);
            if (p is not null)
            {
                lista.Add(p);
            }
        }

        return lista;
    }
}
