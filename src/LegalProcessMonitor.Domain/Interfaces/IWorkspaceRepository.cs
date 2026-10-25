using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;

namespace LegalProcessMonitor.Domain.Interfaces;

public interface IWorkspaceRepository
{
    Task<EstadoWorkspaceTenant> ObterEstadoWorkspaceAsync(string tenantId, CancellationToken ct = default);

    // Configuração do Escritório / Radar OAB
    Task<ConfigRadarOab> SalvarConfigRadarAsync(ConfigRadarOab config, CancellationToken ct = default);

    // Watchlist Multi-Tenant
    Task<ItemWatchlistTenant> SalvarItemWatchlistAsync(ItemWatchlistTenant item, CancellationToken ct = default);
    Task<bool> RemoverItemWatchlistAsync(string tenantId, string numeroProcesso, CancellationToken ct = default);
    Task<IReadOnlyList<ItemWatchlistTenant>> ListarWatchlistAsync(string tenantId, CancellationToken ct = default);

    // Notas Internas de Juízo Multi-Tenant
    Task<NotaJuizoTenant> SalvarNotaJuizoAsync(NotaJuizoTenant nota, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> ListarNotasJuizoAsync(string tenantId, CancellationToken ct = default);

    // Suspensões Forenses Locais (Provimentos/Feriados Municipais/Indisponibilidades)
    Task<SuspensaoForenseLocal> AdicionarSuspensaoLocalAsync(SuspensaoForenseLocal suspensao, CancellationToken ct = default);
    Task<bool> RemoverSuspensaoLocalAsync(string tenantId, string suspensaoId, CancellationToken ct = default);
    Task<IReadOnlyList<SuspensaoForenseLocal>> ListarSuspensoesLocaisAsync(string tenantId, CancellationToken ct = default);

    // Quadro Kanban de Tarefas Processuais
    Task<TarefaProcessual> SalvarTarefaAsync(TarefaProcessual tarefa, CancellationToken ct = default);
    Task<bool> RemoverTarefaAsync(string tenantId, string tarefaId, CancellationToken ct = default);
    Task<IReadOnlyList<TarefaProcessual>> ListarTarefasAsync(string tenantId, CancellationToken ct = default);

    // Radar Diário DJEN
    Task<int> SalvarItensRadarLoteAsync(string tenantId, IEnumerable<ItemRadarDjen> itens, CancellationToken ct = default);
    Task<bool> MarcarItemRadarLidoAsync(string tenantId, string itemId, bool convertidaEmTarefa = false, CancellationToken ct = default);
    Task<IReadOnlyList<ItemRadarDjen>> ListarItensRadarAsync(string tenantId, CancellationToken ct = default);

    // Banco de Teses Jurídicas Multi-Tenant & Histórico de Aplicações nos Autos
    Task<TeseJuridicaTenant> SalvarTeseJuridicaAsync(TeseJuridicaTenant tese, CancellationToken ct = default);
    Task<bool> RemoverTeseJuridicaAsync(string tenantId, string teseId, CancellationToken ct = default);
    Task<IReadOnlyList<TeseJuridicaTenant>> ListarTesesJuridicasAsync(string tenantId, CancellationToken ct = default);
    Task<AplicacaoTeseProcesso> RegistrarAplicacaoTeseAsync(AplicacaoTeseProcesso aplicacao, CancellationToken ct = default);
    Task<IReadOnlyList<AplicacaoTeseProcesso>> ListarAplicacoesTesesAsync(string tenantId, string? numeroProcesso = null, CancellationToken ct = default);
}

public interface IInteligenciaPublicaConnector
{
    Task<IReadOnlyList<JulgamentoRealMagistrado>> BuscarJulgamentosReaisMagistradoAsync(
        string nomeMagistrado,
        string siglaTribunal = "TJSP",
        CancellationToken ct = default);

    Task<FichaPatrimonialParte> ConsultarFichaPatrimonialParteAsync(
        string cnpjOuNomeEmpresa,
        string? municipioComarca = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<SuspensaoForenseLocal>> ObterFeriadosOficiaisAnoAsync(
        string tenantId,
        int ano,
        string siglaTribunal = "TJSP",
        CancellationToken ct = default);
}

public sealed record ResultadoDisparoAlerta(
    bool Sucesso,
    string Canal,
    int StatusCode,
    string MensagemDiagnostico);

public interface IWebhookAlertDispatcher
{
    Task<ResultadoDisparoAlerta> DispararAlertaRadarAsync(
        ConfigRadarOab config,
        IReadOnlyList<ItemRadarDjen> novasIntimacoes,
        CancellationToken ct = default);
}
