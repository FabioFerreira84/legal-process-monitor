using System.Linq;
using FluentAssertions;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.ValueObjects;
using LegalProcessMonitor.Infrastructure.Connectors.Djen;
using LegalProcessMonitor.Infrastructure.Connectors.Esaj;
using Xunit;

namespace LegalProcessMonitor.Tests;

public class EsajOabScraperTests
{
    [Fact]
    public void ExtrairProcessos1Grau_ComHtmlValido_DeveExtrairCnjClasseAssuntoEVaraReais()
    {
        var html1g = @"
            <ul>
                <li>
                    <div id=""divProcesso1"">
                        <a class=""linkProcesso"">1006805-85.2025.8.26.0224</a>
                        <div class=""unj-base-alt nomeParte"">João Paulo Paiva Camacho</div>
                        <div class=""classeProcesso"">Reintegração / Manutenção de Posse</div>
                        <div class=""assuntoPrincipalProcesso"">Esbulho / Turbação / Ameaça</div>
                        <div class=""dataLocalDistribuicaoProcesso"">01/04/2025 - 1ª Vara Cível - Foro de Guarulhos</div>
                    </div>
                </li>
            </ul>";

        var carteira = new AdvogadoCarteira { NumeroOab = "238470SP" };
        EsajOabScraper.ExtrairProcessos1Grau(html1g, carteira);

        carteira.NomeAdvogado.Should().Be("João Paulo Paiva Camacho");
        carteira.Processos.Should().ContainSingle();
        var proc = carteira.Processos[0];
        proc.NumeroCnjFormatado.Should().Be("1006805-85.2025.8.26.0224");
        proc.NumeroCnjLimpo.Should().Be("10068058520258260224");
        proc.Grau.Should().Be(1);
        proc.Classe.Should().Be("Reintegração / Manutenção de Posse (Esbulho / Turbação / Ameaça)");
        proc.DataRecebimento.Should().Be("01/04/2025");
        proc.FonteDados.Should().Be("e-SAJ TJSP 1º Grau");
    }

    [Fact]
    public void ExtrairProcessos2Grau_ComHtmlValido_DeveExtrairClasseRealSemHardcode()
    {
        var html2g = @"
            <ul>
                <li>
                    <div id=""divProcesso2"">
                        <a class=""linkProcesso"">2101760-84.2025.8.26.0000</a>
                        <div class=""classeProcesso"">Apelação Cível</div>
                        <div class=""assuntoPrincipalProcesso"">Contratos Bancários</div>
                        <div class=""dataLocalDistribuicao"">07/04/2025 - 24ª Câmara de Direito Privado</div>
                    </div>
                </li>
            </ul>";

        var carteira = new AdvogadoCarteira { NumeroOab = "238470SP" };
        EsajOabScraper.ExtrairProcessos2Grau(html2g, carteira);

        carteira.Processos.Should().ContainSingle();
        var proc = carteira.Processos[0];
        proc.Grau.Should().Be(2);
        proc.Classe.Should().Be("Apelação Cível (Contratos Bancários)");
        proc.FonteDados.Should().Be("e-SAJ TJSP 2º Grau");
    }

    [Fact]
    public void DjenConnector_AplicarJsonAoProcesso_DeveExtrairPublicacoesEmentasEPartesReais()
    {
        var jsonDjen = @"
        {
          ""status"": ""success"",
          ""count"": 2,
          ""items"": [
            {
              ""id"": 370489236,
              ""data_disponibilizacao"": ""2025-09-04"",
              ""siglaTribunal"": ""TJSP"",
              ""tipoComunicacao"": ""Intimação"",
              ""nomeOrgao"": ""Subseção IX - Intimações de Acórdãos"",
              ""tipoDocumento"": ""Intimação de acórdão"",
              ""nomeClasse"": ""AGRAVO DE INSTRUMENTO"",
              ""numero_processo"": ""21017608420258260000"",
              ""numeroprocessocommascara"": ""2101760-84.2025.8.26.0000"",
              ""link"": ""https://www.dje.tjsp.jus.br"",
              ""texto"": ""INTIMAÇÃO DE ACÓRDÃO - Magistrado(a) Pedro Paulo Maillet Preuss - Deram provimento em parte ao recurso. V. U. - AGRAVO DE INSTRUMENTO. AÇÃO DE MANUTENÇÃO DE POSSE. DECISÃO QUE DETERMINOU A CORREÇÃO DO VALOR DA CAUSA PARA QUE CORRESPONDA AO BENEFÍCIO PATRIMONIAL VISADO."",
              ""destinatarios"": [
                { ""nome"": ""PASCHOAL THOMEU (AGRAVANTE)"", ""polo"": ""A"" },
                { ""nome"": ""LIFE EMPREENDIMENTOS LTDA (AGRAVADO)"", ""polo"": ""P"" }
              ],
              ""destinatarioadvogados"": [
                {
                  ""advogado"": {
                    ""nome"": ""JOAO PAULO PAIVA CAMACHO"",
                    ""numero_oab"": ""238470"",
                    ""uf_oab"": ""SP""
                  }
                }
              ]
            }
          ]
        }";

        var cnj = NumeroProcessoCnj.Criar("2101760-84.2025.8.26.0000");
        var proc = Processo.Criar(cnj);

        DjenComunicacaoConnector.AplicarJsonAoProcesso(jsonDjen, proc);

        proc.DocumentosPublicos.Should().ContainSingle();
        proc.DocumentosPublicos[0].Ementa.Should().Contain("CORREÇÃO DO VALOR DA CAUSA");
        proc.DocumentosPublicos[0].CertidaoPublicacao.Should().Contain("04/09/2025");
        proc.Envolvidos.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void DjenConnector_AplicarJsonACarteiraOab_DeveExtrairProcessosMultiTribunais()
    {
        var jsonDjen = @"
        {
          ""status"": ""success"",
          ""count"": 394,
          ""items"": [
            {
              ""id"": 765092040,
              ""data_disponibilizacao"": ""2026-10-23"",
              ""siglaTribunal"": ""STJ"",
              ""tipoComunicacao"": ""Intimação"",
              ""nomeOrgao"": ""COORDENADORIA DE CLASSIFICAÇÃO E DISTRIBUIÇÃO DE PROCESSOS"",
              ""tipoDocumento"": ""ATA DE DISTRIBUIÇÃO"",
              ""nomeClasse"": ""AGRAVO EM RECURSO ESPECIAL"",
              ""numero_processo"": ""21017608420258260000"",
              ""numeroprocessocommascara"": ""2101760-84.2025.8.26.0000"",
              ""link"": ""https://justica.web.stj.jus.br/api/pcp/documentos/pdf"",
              ""texto"": ""Processo distribuído pelo sistema automático em 22/10/2026."",
              ""destinatarioadvogados"": [
                {
                  ""advogado"": {
                    ""nome"": ""JOAO PAULO PAIVA CAMACHO"",
                    ""numero_oab"": ""238470"",
                    ""uf_oab"": ""SP""
                  }
                }
              ]
            }
          ]
        }";

        var carteira = new AdvogadoCarteira { NumeroOab = "238470SP" };
        DjenComunicacaoConnector.AplicarJsonACarteiraOab(jsonDjen, carteira, "238470", "SP");

        carteira.TotalComunicacoesDjen.Should().Be(394);
        carteira.NomeAdvogado.Should().Be("JOAO PAULO PAIVA CAMACHO");
        carteira.Processos.Should().ContainSingle();
        carteira.Processos[0].Tribunal.Should().Be("STJ");
        carteira.Processos[0].Classe.Should().Contain("AGRAVO EM RECURSO ESPECIAL");
        carteira.Processos[0].FonteDados.Should().Be("DJEN/CNJ Oficial");
    }
}
