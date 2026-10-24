using System;
using FluentAssertions;
using LegalProcessMonitor.Domain.ValueObjects;
using Xunit;

namespace LegalProcessMonitor.Tests;

public class NumeroProcessoCnjTests
{
    [Fact]
    public void Criar_ComNumeroValido_DeveRetornarInstanciaCorreta()
    {
        var rawCnj = "0000001-73.2023.8.26.0100";
        var cnj = NumeroProcessoCnj.Criar(rawCnj);

        cnj.Should().NotBeNull();
        cnj.NumeroFormatado.Should().Be(rawCnj);
        cnj.Sequencial.Should().Be("0000001");
        cnj.DigitoVerificador.Should().Be("73");
        cnj.AnoAjuizamento.Should().Be(2023);
        cnj.OrgaoJustica.Should().Be(8);
        cnj.TribunalId.Should().Be(26);
        cnj.ForoOrigem.Should().Be("0100");
        cnj.SiglaTribunal.Should().Be("TJSP");
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
