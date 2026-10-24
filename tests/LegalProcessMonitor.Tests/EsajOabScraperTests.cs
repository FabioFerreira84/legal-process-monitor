using System.Threading.Tasks;
using LegalProcessMonitor.Infrastructure.Connectors.Esaj;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LegalProcessMonitor.Tests;

public class EsajOabScraperTests
{
    [Fact]
    public async Task ConsultarProcessosPorOab_DeveRetornarCarteiraNormalizada()
    {
        var scraper = new EsajOabScraper(NullLogger<EsajOabScraper>.Instance);

        var resultado = await scraper.ConsultarProcessosPorOabAsync("238.470/SP");

        Assert.NotNull(resultado);
        Assert.Equal("238470SP", resultado.NumeroOab);
        Assert.True(resultado.Processos.Count > 0);
        Assert.Contains(resultado.Processos, p => p.NumeroCnjLimpo.StartsWith("2101760842025") || p.NumeroCnjLimpo.StartsWith("1130342"));
    }
}
