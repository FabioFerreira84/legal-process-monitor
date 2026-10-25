using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Infrastructure.Repositories;
using Xunit;

namespace LegalProcessMonitor.Tests;

public sealed class SqlitePersistenceTests : IDisposable
{
    private readonly string _caminhoBancoTemp;

    public SqlitePersistenceTests()
    {
        _caminhoBancoTemp = Path.Combine(Path.GetTempPath(), $"legalmonitor_test_{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task SqliteWorkspaceRepository_DeveIsolarEstritamenteDadosEntreEscritoriosDistintos()
    {
        var repo = new SqliteWorkspaceRepository($"Data Source={_caminhoBancoTemp}");

        // Escritório Alpha adiciona Watchlist, Nota de Juízo, Suspensão Local e Tarefa Kanban
        await repo.SalvarItemWatchlistAsync(new ItemWatchlistTenant(
            TenantId: "escritorio-alpha",
            NumeroProcesso: "2101760-84.2025.8.26.0000",
            Classe: "Agravo de Instrumento",
            OrgaoJulgador: "24ª Câmara de Direito Privado",
            Tribunal: "TJSP",
            TotalMovimentacoesConhecidas: 28,
            AdicionadoEmUtc: DateTime.UtcNow));

        await repo.SalvarNotaJuizoAsync(new NotaJuizoTenant(
            TenantId: "escritorio-alpha",
            ChaveUnidade: "TJSP::24ª Câmara de Direito Privado",
            ConteudoNota: "Despachar memorial 48h antes da sessão virtual.",
            AtualizadoEmUtc: DateTime.UtcNow));

        await repo.AdicionarSuspensaoLocalAsync(new SuspensaoForenseLocal(
            Id: "susp-1",
            TenantId: "escritorio-alpha",
            Data: new DateOnly(2026, 10, 29),
            Descricao: "Provimento CSM Indisponibilidade",
            ComarcaOuTribunal: "TJSP",
            CriadoEmUtc: DateTime.UtcNow));

        await repo.SalvarTarefaAsync(new TarefaProcessual(
            Id: "task-alpha-1",
            TenantId: "escritorio-alpha",
            NumeroProcesso: "2101760-84.2025.8.26.0000",
            Titulo: "Apresentar Contrarrazões ao AREsp 3319477/SP",
            Descricao: "Intimação disponibilizada no DJEN em 23/10/2026.",
            Responsavel: "Dr. Fabio Ferreira",
            Coluna: StatusTarefaColuna.Intimacao,
            Prioridade: PrioridadeTarefa.Critica,
            PrazoFatal: new DateOnly(2026, 11, 18),
            DiasPrazo: 15,
            PrazoEmDobro: false,
            NumeroProtocolo: null,
            CriadoEmUtc: DateTime.UtcNow,
            AtualizadoEmUtc: DateTime.UtcNow));

        // Escritório Beta consulta seu próprio workspace
        var estadoAlpha = await repo.ObterEstadoWorkspaceAsync("escritorio-alpha");
        var estadoBeta = await repo.ObterEstadoWorkspaceAsync("escritorio-beta");

        estadoAlpha.Watchlist.Should().HaveCount(1);
        estadoAlpha.NotasJuizo.Should().ContainKey("TJSP::24ª Câmara de Direito Privado");
        estadoAlpha.SuspensoesLocais.Should().HaveCount(1);
        estadoAlpha.TarefasKanban.Should().HaveCount(1);

        // Isolamento absoluto: Escritório Beta não enxerga nada do Escritório Alpha
        estadoBeta.Watchlist.Should().BeEmpty();
        estadoBeta.NotasJuizo.Should().BeEmpty();
        estadoBeta.SuspensoesLocais.Should().BeEmpty();
        estadoBeta.TarefasKanban.Should().BeEmpty();
    }

    [Fact]
    public async Task SqliteWorkspaceRepository_KanbanCicloDeVida_DeveMoverColunaERegistrarProtocolo()
    {
        var repo = new SqliteWorkspaceRepository($"Data Source={_caminhoBancoTemp}");

        var inicial = new TarefaProcessual(
            Id: "task-1",
            TenantId: "escritorio-ferreira",
            NumeroProcesso: "2101760-84.2025.8.26.0000",
            Titulo: "Opor Embargos de Declaração",
            Descricao: "Omissão quanto aos honorários recursais",
            Responsavel: "Dr. Fabio Ferreira",
            Coluna: StatusTarefaColuna.Intimacao,
            Prioridade: PrioridadeTarefa.Alta,
            PrazoFatal: new DateOnly(2026, 11, 4),
            DiasPrazo: 5,
            PrazoEmDobro: false,
            NumeroProtocolo: null,
            CriadoEmUtc: DateTime.UtcNow,
            AtualizadoEmUtc: DateTime.UtcNow);

        await repo.SalvarTarefaAsync(inicial);

        // Avança para Protocolado informando o recibo do protocolo e-SAJ/PJe
        var protocolada = inicial with
        {
            Coluna = StatusTarefaColuna.Protocolado,
            NumeroProtocolo = "WJMJ264099182-0",
            AtualizadoEmUtc = DateTime.UtcNow
        };
        await repo.SalvarTarefaAsync(protocolada);

        var lista = await repo.ListarTarefasAsync("escritorio-ferreira");
        lista.Should().ContainSingle();
        lista[0].Coluna.Should().Be(StatusTarefaColuna.Protocolado);
        lista[0].NumeroProtocolo.Should().Be("WJMJ264099182-0");
    }

    [Fact]
    public async Task SqliteWorkspaceRepository_RadarDjen_DeveEvitarDuplicatasEMarcarComoConvertida()
    {
        var repo = new SqliteWorkspaceRepository($"Data Source={_caminhoBancoTemp}");

        var item = new ItemRadarDjen(
            Id: "djen-765092040",
            TenantId: "escritorio-ferreira",
            OabOrigem: "238470/SP",
            NumeroProcesso: "2101760-84.2025.8.26.0000",
            Tribunal: "STJ",
            OrgaoJulgador: "Presidência STJ",
            DataDisponibilizacao: new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc),
            TipoComunicacao: "Intimação",
            TeorResumo: "Distribuído AREsp 3319477/SP ao Min. Antonio Carlos Ferreira.",
            LinkInteiroTeor: "https://comunica.pje.jus.br",
            Lida: false,
            ConvertidaEmTarefa: false,
            CapturadoEmUtc: DateTime.UtcNow);

        var inseridos1 = await repo.SalvarItensRadarLoteAsync("escritorio-ferreira", new[] { item });
        var inseridos2 = await repo.SalvarItensRadarLoteAsync("escritorio-ferreira", new[] { item });

        inseridos1.Should().Be(1);
        inseridos2.Should().Be(0); // Idempotente: não duplica a mesma intimação

        await repo.MarcarItemRadarLidoAsync("escritorio-ferreira", "djen-765092040", convertidaEmTarefa: true);

        var radar = await repo.ListarItensRadarAsync("escritorio-ferreira");
        radar.Should().ContainSingle();
        radar[0].Lida.Should().BeTrue();
        radar[0].ConvertidaEmTarefa.Should().BeTrue();
    }

    [Fact]
    public async Task SqliteProcessoRepository_DeveSemearProcessosReaisEPersistirNovosAutos()
    {
        var repo = new SqliteProcessoRepository($"Data Source={_caminhoBancoTemp}");

        var semeados = await repo.ListarRecentesAsync(50);
        semeados.Should().HaveCountGreaterThanOrEqualTo(2);

        // Persiste um novo processo (1º grau Guarulhos) e verifica em uma segunda instância do repositório
        await repo.SalvarOuAtualizarAsync(new Processo
        {
            NumeroCnjFormatado = "1006805-85.2025.8.26.0224",
            NumeroCnjLimpo = "10068058520258260224",
            Tribunal = "TJSP",
            Classe = "Reintegração / Manutenção de Posse",
            OrgaoJulgador = "1ª Vara Cível de Guarulhos"
        });

        var repoReaberto = new SqliteProcessoRepository($"Data Source={_caminhoBancoTemp}");
        var todosAposReabrir = await repoReaberto.ListarRecentesAsync(50);
        todosAposReabrir.Should().HaveCount(3);

        var proc = await repoReaberto.ObterPorNumeroAsync("21017608420258260000");
        proc.Should().NotBeNull();
        proc!.DocumentosPublicos.Should().NotBeEmpty();
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_caminhoBancoTemp))
                File.Delete(_caminhoBancoTemp);
        }
        catch
        {
            // Ignora bloqueio de pool do SQLite no teardown de arquivo temp
        }
    }
}
