using System;
using FluentAssertions;
using LegalProcessMonitor.Domain.ValueObjects;
using Xunit;

namespace LegalProcessMonitor.Tests;

public class NumeroProcessoCnjTests
{
    [Fact]
    public void CalcularDigitoVerificador_DeveCalcularEValidar()
    {
        var seq = "0000001";
        var ano = 2023;
        var justica = 8;
        var tribunal = 26;
        var foro = "0100";

        var dv = NumeroProcessoCnj.CalcularDigitoVerificador(seq, ano, justica, tribunal, foro);
        var numeroCompleto = $"{seq}-{dv}.{ano}.{justica}.{tribunal:D2}.{foro}";

        var cnj = NumeroProcessoCnj.Criar(numeroCompleto);

        cnj.Should().NotBeNull();
        cnj.DigitoVerificador.Should().Be(dv);
        cnj.SiglaTribunal.Should().Be("TJSP");
        cnj.NumeroFormatado.Should().Be(numeroCompleto);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("0000001-99.2023.8.26.0100")] // DV inválido
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Criar_ComNumeroInvalido_DeveLancarArgumentException(string? rawCnj)
    {
        Action act = () => NumeroProcessoCnj.Criar(rawCnj!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryParse_ComNumeroInvalido_DeveRetornarFalse()
    {
        var rawCnj = "0000001-99.2023.8.26.0100";
        var success = NumeroProcessoCnj.TryParse(rawCnj, out var cnj);

        success.Should().BeFalse();
        cnj.Should().BeNull();
    }
}
