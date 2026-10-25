using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LegalProcessMonitor.Domain.Entities;

public enum StatusTarefaColuna
{
    Intimacao = 0,
    Elaboracao = 1,
    Revisao = 2,
    Protocolado = 3
}

public enum PrioridadeTarefa
{
    Baixa = 0,
    Media = 1,
    Alta = 2,
    Critica = 3
}

public static partial class TenantValidador
{
    [GeneratedRegex(@"^[a-zA-Z0-9_-]{3,64}$", RegexOptions.Compiled)]
    private static partial Regex TenantIdRegex();

    public static string NormalizarEValidar(string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("O identificador do escritório (TenantId) é obrigatório.", nameof(tenantId));

        var limpo = tenantId.Trim().ToLowerInvariant();
        if (!TenantIdRegex().IsMatch(limpo))
            throw new ArgumentException("O TenantId deve conter entre 3 e 64 caracteres alfanuméricos, hífens ou sublinhados.", nameof(tenantId));

        return limpo;
    }
}

public sealed record ItemWatchlistTenant(
    string TenantId,
    string NumeroProcesso,
    string Classe,
    string OrgaoJulgador,
    string Tribunal,
    int TotalMovimentacoesConhecidas,
    DateTime AdicionadoEmUtc,
    bool TemNovidade = false);

public sealed record NotaJuizoTenant(
    string TenantId,
    string ChaveUnidade,
    string ConteudoNota,
    DateTime AtualizadoEmUtc);

public sealed record SuspensaoForenseLocal(
    string Id,
    string TenantId,
    DateOnly Data,
    string Descricao,
    string ComarcaOuTribunal,
    DateTime CriadoEmUtc);

public sealed record TarefaProcessual(
    string Id,
    string TenantId,
    string NumeroProcesso,
    string Titulo,
    string Descricao,
    string Responsavel,
    StatusTarefaColuna Coluna,
    PrioridadeTarefa Prioridade,
    DateOnly PrazoFatal,
    int DiasPrazo,
    bool PrazoEmDobro,
    string? NumeroProtocolo,
    DateTime CriadoEmUtc,
    DateTime AtualizadoEmUtc);

public sealed record ConfigRadarOab(
    string TenantId,
    string NomeEscritorio,
    IReadOnlyList<string> InscricoesOab,
    string? WebhookUrl,
    string? TelegramBotToken,
    string? TelegramChatId,
    bool Ativo,
    DateTime? UltimaSincronizacaoUtc);

public sealed record ItemRadarDjen(
    string Id,
    string TenantId,
    string OabOrigem,
    string NumeroProcesso,
    string Tribunal,
    string OrgaoJulgador,
    DateTime DataDisponibilizacao,
    string TipoComunicacao,
    string TeorResumo,
    string LinkInteiroTeor,
    bool Lida,
    bool ConvertidaEmTarefa,
    DateTime CapturadoEmUtc);

public sealed record EstadoWorkspaceTenant(
    string TenantId,
    ConfigRadarOab Configuracao,
    IReadOnlyList<ItemWatchlistTenant> Watchlist,
    IReadOnlyDictionary<string, string> NotasJuizo,
    IReadOnlyList<SuspensaoForenseLocal> SuspensoesLocais,
    IReadOnlyList<TarefaProcessual> TarefasKanban,
    IReadOnlyList<ItemRadarDjen> ItensRadar);
