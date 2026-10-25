using System;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Infrastructure.Connectors.Alerts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LegalProcessMonitor.Tests;

public class WebhookAlertDispatcherTests
{
    [Theory]
    [InlineData("http://localhost:8080/hook")]
    [InlineData("http://127.0.0.1/admin")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("ftp://example.com/hook")]
    public async Task DispararAlertaRadarAsync_ComUrlInseguraOuLocalhost_DeveBloquearContraSsrf(string urlInsegura)
    {
        var dispatcher = new WebhookAlertDispatcher(new HttpClient(), NullLogger<WebhookAlertDispatcher>.Instance);
        var config = new ConfigRadarOab(
            TenantId: "escritorio-ferreira",
            NomeEscritorio: "Ferreira Advogados",
            InscricoesOab: new[] { "238470/SP" },
            WebhookUrl: urlInsegura,
            TelegramBotToken: null,
            TelegramChatId: null,
            Ativo: true,
            UltimaSincronizacaoUtc: DateTime.UtcNow);

        var itens = new[]
        {
            new ItemRadarDjen(
                Id: "djen-1",
                TenantId: "escritorio-ferreira",
                OabOrigem: "238470/SP",
                NumeroProcesso: "2101760-84.2025.8.26.0000",
                Tribunal: "STJ",
                OrgaoJulgador: "Presidência STJ",
                DataDisponibilizacao: DateTime.UtcNow,
                TipoComunicacao: "Intimação",
                TeorResumo: "Intimação de distribuição",
                LinkInteiroTeor: "https://comunica.pje.jus.br",
                Lida: false,
                ConvertidaEmTarefa: false,
                CapturadoEmUtc: DateTime.UtcNow)
        };

        var resultado = await dispatcher.DispararAlertaRadarAsync(config, itens);
        resultado.Sucesso.Should().BeFalse();
        resultado.MensagemDiagnostico.Should().Contain("bloqueada");
    }
}
