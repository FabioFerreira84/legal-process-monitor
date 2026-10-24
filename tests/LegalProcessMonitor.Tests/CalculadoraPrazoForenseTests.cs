using System;
using FluentAssertions;
using LegalProcessMonitor.Domain.Services;
using Xunit;

namespace LegalProcessMonitor.Tests;

public class CalculadoraPrazoForenseTests
{
    [Theory]
    [InlineData(2025, 11, 20)] // Consciência Negra - Lei 14.759/2023
    [InlineData(2026, 11, 20)] // Consciência Negra - Lei 14.759/2023
    [InlineData(2025, 3, 3)]   // Segunda de Carnaval 2025
    [InlineData(2025, 3, 4)]   // Terça de Carnaval 2025
    [InlineData(2025, 4, 17)]  // Quinta-feira Santa 2025 (Feriado Forense)
    [InlineData(2025, 4, 18)]  // Sexta-feira Santa 2025
    [InlineData(2025, 6, 19)]  // Corpus Christi 2025
    [InlineData(2026, 2, 16)]  // Segunda de Carnaval 2026
    [InlineData(2026, 2, 17)]  // Terça de Carnaval 2026
    [InlineData(2026, 4, 3)]   // Sexta-feira Santa 2026
    [InlineData(2026, 6, 4)]   // Corpus Christi 2026
    [InlineData(2026, 8, 11)]  // Dia do Advogado / Criação dos Cursos Jurídicos (Feriado Forense)
    [InlineData(2026, 12, 8)]  // Dia da Justiça (Feriado Forense)
    [InlineData(2025, 12, 20)] // Início Recesso Art. 220 CPC
    [InlineData(2026, 1, 20)]  // Fim Recesso Art. 220 CPC
    public void EhDiaUtilForense_EmFeriadosNacionaisMoveisERecesso_DeveRetornarFalse(int ano, int mes, int dia)
    {
        var data = new DateOnly(ano, mes, dia);
        CalculadoraPrazoForense.EhDiaUtilForense(data, "TJSP", out var motivo).Should().BeFalse();
        motivo.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void EhDiaUtilForense_Em09JulhoNoTjsp_DeveSerFeriadoEstadual()
    {
        var noveJulho = new DateOnly(2026, 7, 9); // Quinta-feira
        CalculadoraPrazoForense.EhDiaUtilForense(noveJulho, "TJSP", out var motivoSp).Should().BeFalse();
        motivoSp.Should().Contain("Revolução Constitucionalista");

        // Em outro estado (ex: TJRJ), 09/07 é dia útil normal
        CalculadoraPrazoForense.EhDiaUtilForense(noveJulho, "TJRJ", out _).Should().BeTrue();
    }

    [Fact]
    public void Calcular_ComDisponibilizacaoDjenNaSexta_DevePublicarSegundaEIniciarTerca()
    {
        // Caso real do STJ/DJEN: disponibilizado em 23/10/2026 (sexta-feira)
        var disponibilizacao = new DateOnly(2026, 10, 23);
        var referenciaHoje = new DateOnly(2026, 10, 24);

        var resultado = CalculadoraPrazoForense.Calcular(
            dataMarco: disponibilizacao,
            diasPrazoBase: 15,
            ehDisponibilizacaoDjen: true,
            prazoEmDobro: false,
            siglaTribunal: "TJSP",
            dataReferenciaHoje: referenciaHoje);

        // Publicação: 1º dia útil seguinte = 26/10/2026 (segunda-feira)
        resultado.DataPublicacao.Should().Be(new DateOnly(2026, 10, 26));
        // Início do prazo: 1º dia útil seguinte à publicação = 27/10/2026 (terça-feira)
        resultado.DataInicioPrazo.Should().Be(new DateOnly(2026, 10, 27));

        // 15 dias úteis a partir de 27/10/2026 (incluindo 27/10), pulando:
        // - 28/10/2026 (Dia do Servidor Público - suspenso no Judiciário)
        // - 02/11/2026 (Finados - segunda-feira)
        // - 20/11/2026 (Consciência Negra - sexta-feira)
        // Dias úteis contados:
        // 1: 27/10, 2: 29/10, 3: 30/10
        // 4: 03/11, 5: 04/11, 6: 05/11, 7: 06/11
        // 8: 09/11, 9: 10/11, 10: 11/11, 11: 12/11, 12: 13/11
        // 13: 16/11, 14: 17/11, 15: 18/11
        resultado.DataTermoFatal.Should().Be(new DateOnly(2026, 11, 18));
        resultado.DiasUteisRestantes.Should().Be(16); // De 24/10 (sábado) até 18/11 há 26/10 + 15 dias úteis = 16 dias úteis
    }

    [Fact]
    public void Calcular_AtravessandoRecessoForenseArt220_DeveSuspenderDe20DezA20Jan()
    {
        // Disponibilizado em 17/12/2025 (quarta-feira)
        // Publicado em 18/12/2025 (quinta-feira)
        // 1º dia útil (início): 19/12/2025 (sexta-feira) -> conta 1 dia útil
        // De 20/12/2025 a 20/01/2026 -> suspenso (Art. 220 CPC)
        // Retoma em 21/01/2026 (quarta-feira) -> 2º dia útil
        // Para Embargos de Declaração (5 dias úteis):
        // 1: 19/12/2025
        // 2: 21/01/2026 (qua)
        // 3: 22/01/2026 (qui)
        // 4: 23/01/2026 (sex)
        // 5: 26/01/2026 (seg) -> em TJSP Capital 25/01 é domingo
        var resultado = CalculadoraPrazoForense.Calcular(
            dataMarco: new DateOnly(2025, 12, 17),
            diasPrazoBase: 5,
            ehDisponibilizacaoDjen: true,
            prazoEmDobro: false,
            siglaTribunal: "TJSP",
            dataReferenciaHoje: new DateOnly(2025, 12, 22));

        resultado.DataPublicacao.Should().Be(new DateOnly(2025, 12, 18));
        resultado.DataInicioPrazo.Should().Be(new DateOnly(2025, 12, 19));
        resultado.DataTermoFatal.Should().Be(new DateOnly(2026, 1, 26));
        // Em 22/12/2025 (durante o recesso), faltam exatamente os 4 dias úteis de janeiro (21, 22, 23, 26)
        resultado.DiasUteisRestantes.Should().Be(4);
    }

    [Fact]
    public void Calcular_ComPrazoEmDobroFazendaPublica_DeveMultiplicarPorDois()
    {
        var resultado = CalculadoraPrazoForense.Calcular(
            dataMarco: new DateOnly(2026, 10, 23),
            diasPrazoBase: 15,
            ehDisponibilizacaoDjen: true,
            prazoEmDobro: true,
            siglaTribunal: "TRF3",
            dataReferenciaHoje: new DateOnly(2026, 10, 24));

        resultado.DiasUteisTotal.Should().Be(30);
        resultado.BaseLegal.Should().Contain("Art. 183");
    }
}
