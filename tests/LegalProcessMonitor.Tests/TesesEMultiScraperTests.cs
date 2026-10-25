using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Services;
using LegalProcessMonitor.Domain.ValueObjects;
using LegalProcessMonitor.Infrastructure.Connectors.Esaj;
using LegalProcessMonitor.Infrastructure.Connectors.Inteligencia;
using LegalProcessMonitor.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LegalProcessMonitor.Tests;

public sealed class TesesEMultiScraperTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connString;

    public TesesEMultiScraperTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lpm-teses-test-{Guid.NewGuid():N}.db");
        _connString = $"Data Source={_dbPath}";
    }

    [Theory]
    [InlineData("8", "24", "TJSC")]
    [InlineData("8", "12", "TJMS")]
    [InlineData("8", "06", "TJCE")]
    [InlineData("8", "04", "TJAM")]
    [InlineData("8", "02", "TJAL")]
    [InlineData("8", "01", "TJAC")]
    [InlineData("8", "07", "TJDFT")]
    [InlineData("5", "02", "TRT2")]
    [InlineData("5", "15", "TRT15")]
    public void IdentificarSiglaTribunal_DeveMapearTodosOs27TJsE24TRTs(string justicaStr, string tribStr, string siglaEsperada)
    {
        var sigla = NumeroProcessoCnj.IdentificarSiglaTribunal(int.Parse(justicaStr), int.Parse(tribStr));
        sigla.Should().Be(siglaEsperada);
    }

    [Theory]
    [InlineData("TJSP")]
    [InlineData("TJSC")]
    [InlineData("TJMS")]
    [InlineData("TJCE")]
    [InlineData("TJAM")]
    [InlineData("TJAL")]
    [InlineData("TJAC")]
    public void EsajDocumentScraper_DeveSuportarTodosOs7EstadosMapeados(string sigla)
    {
        var portais = EsajDocumentScraper.ObterPortaisMapeados();
        portais.ContainsKey(sigla).Should().BeTrue();
        portais[sigla].BaseHost.Should().StartWith("https://");
    }

    [Fact]
    public async Task BancoDeTeses_DevePersistirPorTenantEIncrementarContadorDeUsosAoAplicarNoProcesso()
    {
        var repo = new SqliteWorkspaceRepository(_connString, semearDemoPadrao: true);

        // Escritório padrão já vem semeado com 4 teses e 1 aplicação
        var estadoDemo = await repo.ObterEstadoWorkspaceAsync("escritorio-ferreira");
        estadoDemo.TesesJuridicas.Should().NotBeNull();
        estadoDemo.TesesJuridicas!.Count.Should().BeGreaterThanOrEqualTo(4);
        estadoDemo.HistoricoAplicacoesTeses.Should().NotBeNull();
        estadoDemo.HistoricoAplicacoesTeses!.Count.Should().BeGreaterThanOrEqualTo(1);

        // Tenant B deve começar limpo (isolamento multi-tenant)
        var tesesTenantB = await repo.ListarTesesJuridicasAsync("escritorio-silva");
        tesesTenantB.Should().BeEmpty();

        // Cadastra tese no Tenant B e aplica em um processo
        var novaTese = await repo.SalvarTeseJuridicaAsync(new TeseJuridicaTenant(
            Id: "tese-silva-01",
            TenantId: "escritorio-silva",
            Titulo: "Impenhorabilidade da Pequena Propriedade Rural Familiar",
            AreaDireito: "Direito Agrário / Execução",
            EventosGatilho: new[] { "Penhora", "Imóvel Rural", "Execução" },
            FundamentacaoLegal: "Art. 5º, XXVI, CF/88 e Art. 833, VIII, CPC",
            PrecedenteChave: "STF — Tema 961 de Repercussão Geral (ARE 1.038.507)",
            ModeloArgumentacao: "A pequena propriedade rural trabalhada pela família é absolutamente impenhorável.",
            TotalUsos: 0,
            UltimoProcessoAplicado: null,
            AtualizadoEmUtc: DateTime.UtcNow));

        novaTese.TotalUsos.Should().Be(0);

        await repo.RegistrarAplicacaoTeseAsync(new AplicacaoTeseProcesso(
            Id: "aplic-silva-01",
            TenantId: "escritorio-silva",
            TeseId: "tese-silva-01",
            TituloTese: novaTese.Titulo,
            NumeroProcesso: "2101760-84.2025.8.26.0000",
            EventoContexto: "Impugnação à Penhora de Imóvel",
            ResultadoEsperadoOuObtido: "Efeito suspensivo deferido",
            Responsavel: "Dr. Carlos Silva",
            AplicadaEmUtc: DateTime.UtcNow));

        var tesesAtualizadas = await repo.ListarTesesJuridicasAsync("escritorio-silva");
        tesesAtualizadas.Should().ContainSingle();
        tesesAtualizadas[0].TotalUsos.Should().Be(1);
        tesesAtualizadas[0].UltimoProcessoAplicado.Should().Be("2101760-84.2025.8.26.0000");
    }

    [Fact]
    public async Task MotorRecomendacaoTese_DeveRecomendarTeseDeAREspEPossessoriaParaOProcessoReal2101760()
    {
        var repo = new SqliteWorkspaceRepository(_connString, semearDemoPadrao: true);
        var procRepo = new InMemoryProcessoRepository();
        var proc = await procRepo.ObterPorNumeroAsync("21017608420258260000");
        proc.Should().NotBeNull();

        var teses = await repo.ListarTesesJuridicasAsync("escritorio-ferreira");
        var aplicacoes = await repo.ListarAplicacoesTesesAsync("escritorio-ferreira");

        var recomendacoes = MotorRecomendacaoTese.RecomendarParaProcesso(proc!, teses, aplicacoes);
        recomendacoes.Should().NotBeEmpty();

        // As duas primeiras devem ser a tese possessória (Art. 561) e a tese de AREsp/STJ (Súmula 7/182)
        var topIds = recomendacoes.Take(2).Select(r => r.Tese.Id).ToList();
        topIds.Should().Contain("tese-possessoria-requisitos-561-cpc");
        topIds.Should().Contain("tese-stj-distinguishing-sumula-7");
        recomendacoes[0].ScoreAderencia.Should().BeGreaterThanOrEqualTo(75);
    }

    [Fact]
    public void ParseFichaBrasilApiCnpj_DeveExtrairRazaoSocialCapitalSocialEQsa()
    {
        const string jsonSample = """
        {
          "cnpj": "43002534000195",
          "razao_social": "COOPERATIVA AGRICOLA MISTA DE ADAMANTINA",
          "nome_fantasia": "CAMDA",
          "descricao_situacao_cadastral": "ATIVA",
          "capital_social": 85000000.00,
          "porte": "DEMAIS",
          "natureza_juridica": "Cooperativa",
          "cnae_fiscal_descricao": "Comércio atacadista de defensivos agrícolas, adubos, fertilizantes e corretivos do solo",
          "logradouro": "RUA KIYOSHI TAKAHASHI",
          "numero": "305",
          "bairro": "VILA INDUSTRIAL",
          "cep": "17800000",
          "municipio": "ADAMANTINA",
          "uf": "SP",
          "qsa": [
            {
              "nome_socio": "DIRETOR PRESIDENTE EXEMPLO",
              "qualificacao_socio": "Presidente",
              "faixa_etaria": "61 a 70 anos"
            }
          ]
        }
        """;

        var ficha = InteligenciaPublicaConnector.ParseFichaBrasilApiCnpj(jsonSample, "CAMDA");
        ficha.CnpjFormatado.Should().Be("43.002.534/0001-95");
        ficha.RazaoSocial.Should().Be("COOPERATIVA AGRICOLA MISTA DE ADAMANTINA");
        ficha.CapitalSocial.Should().Be(85000000m);
        ficha.QuadroSocietario.Should().ContainSingle();
        ficha.EnderecoDiligenciaCitacao.Should().Contain("ADAMANTINA/SP");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
            if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
        }
        catch
        {
            // Ignore cleanup lock
        }
    }
}
