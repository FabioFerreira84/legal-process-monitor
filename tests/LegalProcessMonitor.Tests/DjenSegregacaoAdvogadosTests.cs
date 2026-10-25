using System.Text.Json;
using FluentAssertions;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Infrastructure.Connectors.Djen;
using Xunit;

namespace LegalProcessMonitor.Tests;

public sealed class DjenSegregacaoAdvogadosTests
{
    [Fact]
    public void SegregarEnvolvidosEDestinatarios_CasoRealGeiceEllen_DeveSegregarPoloAtivoEPassivoCorretamente()
    {
        // Arrange - JSON real do DJEN do processo 0814314-72.2026.8.19.0204 (TJRJ Bangu)
        const string jsonDestinatarios = """
        [
            { "comunicacao_id": 763362147, "nome": "GEICE ELLEN BARBOSA", "polo": "A" },
            { "comunicacao_id": 763362147, "nome": "MARABU DECOR MOVEIS E ESTOFADOS LTDA", "polo": "P" },
            { "comunicacao_id": 763362147, "nome": "SHPS TECNOLOGIA E SERVICOS LTDA.", "polo": "P" }
        ]
        """;

        const string jsonAdvogados = """
        [
            {
                "id": 1438102787,
                "advogado": { "nome": "DANIEL DOMINGUES DE FREITAS", "numero_oab": "248324", "uf_oab": "SP" }
            },
            {
                "id": 1438102766,
                "advogado": { "nome": "GEICE ELLEN BARBOSA", "numero_oab": "252088", "uf_oab": "RJ" }
            },
            {
                "id": 1438102720,
                "advogado": { "nome": "HEBERT APARECIDO JORGETI", "numero_oab": "200627", "uf_oab": "SP" }
            }
        ]
        """;

        const string texto = "Venha a procuraçãoquedeve apresentar data inferior a três meses. Regularize o autor, após conclusos.";

        using var docDest = JsonDocument.Parse(jsonDestinatarios);
        using var docAdv = JsonDocument.Parse(jsonAdvogados);

        // Act
        var envolvidos = DjenComunicacaoConnector.SegregarEnvolvidosEDestinatarios(
            docDest.RootElement,
            docAdv.RootElement,
            texto
        );

        // Assert
        envolvidos.Should().HaveCount(3);

        // 1. Polo Ativo: GEICE ELLEN BARBOSA
        var autora = envolvidos.FirstOrDefault(e => e.Nome == "GEICE ELLEN BARBOSA");
        autora.Should().NotBeNull();
        autora!.TipoPolo.Should().Be("Ativo");
        autora.Papel.Should().Contain("Causa Própria");
        autora.Oab.Should().NotBeNull();
        autora.Oab.Should().Contain("GEICE ELLEN BARBOSA (OAB 252088/RJ)");
        autora.Oab.Should().NotContain("DANIEL DOMINGUES");
        autora.Oab.Should().NotContain("HEBERT APARECIDO");

        // 2. Polo Passivo: MARABU DECOR MOVEIS E ESTOFADOS LTDA
        var reu1 = envolvidos.FirstOrDefault(e => e.Nome == "MARABU DECOR MOVEIS E ESTOFADOS LTDA");
        reu1.Should().NotBeNull();
        reu1!.TipoPolo.Should().Be("Passivo");
        reu1.Oab.Should().NotBeNull();
        reu1.Oab.Should().Contain("DANIEL DOMINGUES DE FREITAS (OAB 248324/SP)");
        reu1.Oab.Should().Contain("HEBERT APARECIDO JORGETI (OAB 200627/SP)");
        reu1.Oab.Should().NotContain("GEICE ELLEN BARBOSA");

        // 3. Polo Passivo: SHPS TECNOLOGIA E SERVICOS LTDA.
        var reu2 = envolvidos.FirstOrDefault(e => e.Nome == "SHPS TECNOLOGIA E SERVICOS LTDA.");
        reu2.Should().NotBeNull();
        reu2!.TipoPolo.Should().Be("Passivo");
        reu2.Oab.Should().NotBeNull();
        reu2.Oab.Should().Contain("DANIEL DOMINGUES DE FREITAS (OAB 248324/SP)");
        reu2.Oab.Should().Contain("HEBERT APARECIDO JORGETI (OAB 200627/SP)");
        reu2.Oab.Should().NotContain("GEICE ELLEN BARBOSA");
    }

    [Fact]
    public void AplicarJsonAoProcesso_ComPayloadDjen_DevePreencherEnvolvidosSegregados()
    {
        // Arrange
        const string jsonDjenCompleto = """
        {
            "status": "success",
            "count": 1,
            "items": [
                {
                    "id": 763362147,
                    "siglaTribunal": "TJRJ",
                    "nomeOrgao": "17º Juizado Especial Cível da Regional de Bangu",
                    "nomeClasse": "PROCEDIMENTO DO JUIZADO ESPECIAL CÍVEL",
                    "tipoDocumento": "Despacho",
                    "datadisponibilizacao": "21/10/2026",
                    "texto": "Venha a procuração. Regularize o autor.",
                    "numero_processo": "08143147220268190204",
                    "numeroprocessocommascara": "0814314-72.2026.8.19.0204",
                    "destinatarios": [
                        { "nome": "GEICE ELLEN BARBOSA", "polo": "A" },
                        { "nome": "MARABU DECOR MOVEIS E ESTOFADOS LTDA", "polo": "P" },
                        { "nome": "SHPS TECNOLOGIA E SERVICOS LTDA.", "polo": "P" }
                    ],
                    "destinatarioadvogados": [
                        { "advogado": { "nome": "DANIEL DOMINGUES DE FREITAS", "numero_oab": "248324", "uf_oab": "SP" } },
                        { "advogado": { "nome": "GEICE ELLEN BARBOSA", "numero_oab": "252088", "uf_oab": "RJ" } },
                        { "advogado": { "nome": "HEBERT APARECIDO JORGETI", "numero_oab": "200627", "uf_oab": "SP" } }
                    ]
                }
            ]
        }
        """;

        var processo = new Processo
        {
            NumeroCnjLimpo = "08143147220268190204",
            NumeroCnjFormatado = "0814314-72.2026.8.19.0204",
            Tribunal = "TJRJ"
        };

        // Act
        DjenComunicacaoConnector.AplicarJsonAoProcesso(jsonDjenCompleto, processo);

        // Assert
        processo.Envolvidos.Should().HaveCount(3);
        var autora = processo.Envolvidos.First(e => e.TipoPolo == "Ativo");
        autora.Nome.Should().Be("GEICE ELLEN BARBOSA");
        autora.Oab.Should().Be("GEICE ELLEN BARBOSA (OAB 252088/RJ)");

        var reus = processo.Envolvidos.Where(e => e.TipoPolo == "Passivo").ToList();
        reus.Should().HaveCount(2);
        foreach (var reu in reus)
        {
            reu.Oab.Should().NotContain("GEICE ELLEN");
            reu.Oab.Should().Contain("DANIEL DOMINGUES");
            reu.Oab.Should().Contain("HEBERT APARECIDO");
        }
    }
}
