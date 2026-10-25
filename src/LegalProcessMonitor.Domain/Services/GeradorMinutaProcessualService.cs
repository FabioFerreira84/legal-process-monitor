using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using LegalProcessMonitor.Domain.Entities;

namespace LegalProcessMonitor.Domain.Services;

/// <summary>
/// Motor de geração automática de minutas e peças processuais contextualizadas (.DOC Word ABNT/Forense).
/// Cruza os dados reais da capa do processo, o cálculo matemático de tempestividade CPC/2015,
/// a tese jurídica selecionada e a jurisprudência real do magistrado/órgão julgador.
/// </summary>
public static class GeradorMinutaProcessualService
{
    public static MinutaProcessualGerada GerarMinutaContextualizada(
        string tenantId,
        string numeroProcesso,
        string? classeProcessual,
        string? assuntoPrincipal,
        string? orgaoJulgador,
        string? poloAtivo,
        string? poloPassivo,
        string? ultimoAndamento,
        DateOnly? dataDisponibilizacaoDjen,
        int diasPrazo,
        TeseJuridicaTenant tese,
        ConfigRadarOab? configEscritorio = null,
        IEnumerable<FeriadoOuSuspensao>? suspensoesLocais = null,
        IReadOnlyList<JulgamentoRealMagistrado>? precedentesRecentesJuizo = null)
    {
        var cnj = string.IsNullOrWhiteSpace(numeroProcesso) ? "0000000-00.0000.0.00.0000" : numeroProcesso.Trim();
        var classe = string.IsNullOrWhiteSpace(classeProcessual) ? "Procedimento Comum Cível" : classeProcessual.Trim();
        var assunto = string.IsNullOrWhiteSpace(assuntoPrincipal) ? "Matéria Cível / Empresarial" : assuntoPrincipal.Trim();
        var orgao = string.IsNullOrWhiteSpace(orgaoJulgador) ? "Vara Cível / Câmara de Direito Privado" : orgaoJulgador.Trim();
        var autor = string.IsNullOrWhiteSpace(poloAtivo) ? "PARTE REQUERENTE / AGRAVADA" : poloAtivo.Trim();
        var reu = string.IsNullOrWhiteSpace(poloPassivo) ? "PARTE REQUERIDA / AGRAVANTE" : poloPassivo.Trim();
        var andamento = string.IsNullOrWhiteSpace(ultimoAndamento) ? "Intimação disponibilizada no Diário de Justiça Eletrônico Nacional (DJEN)" : ultimoAndamento.Trim();

        var nomeEscritorio = configEscritorio?.NomeEscritorio ?? "Advocacia & Consultoria Jurídica";
        var oabPrincipal = configEscritorio?.InscricoesOab?.FirstOrDefault() ?? "OAB/UF";

        // 1. Determinar o Endereçamento Oficial e o Título da Peça conforme o Órgão Julgador e a Tese
        var (enderecamento, tituloPeca, verboManifestacao) = DeterminarEnderecamentoETitulo(orgao, classe, andamento, tese);

        // 2. Calcular Tempestividade Matemática (CPC/2015 + Lei 14.759/2023 + Res. CNJ 455/2022)
        var marcoData = dataDisponibilizacaoDjen ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        var prazoBase = diasPrazo > 0 ? diasPrazo : 15;
        var calculoPrazo = CalculadoraPrazoForense.Calcular(
            dataMarco: marcoData,
            diasPrazoBase: prazoBase,
            ehDisponibilizacaoDjen: true,
            prazoEmDobro: false,
            siglaTribunal: ExtrairSiglaTribunal(orgao),
            suspensoesLocais: suspensoesLocais);

        var feriadosTexto = calculoPrazo.FeriadosConsiderados.Count > 0
            ? $"observados os feriados/suspensões forenses incidentes no período ({string.Join("; ", calculoPrazo.FeriadosConsiderados.Select(f => $"{f.Data:dd/MM/yyyy} — {f.Descricao}"))})"
            : "computados exclusivamente os dias úteis forenses (Art. 219, caput, do CPC/2015)";

        var topicoTempestividade =
            $"A presente manifestação é plenamente tempestiva. Com efeito, a disponibilização do ato judicial no Diário de Justiça Eletrônico Nacional (DJEN) ocorreu em {calculoPrazo.DataDisponibilizacao:dd/MM/yyyy}, " +
            $"considerando-se como data oficial de publicação o primeiro dia útil subsequente, qual seja, {calculoPrazo.DataPublicacao:dd/MM/yyyy} (Art. 224, § 2º, do CPC/2015 c/c Art. 11, § 3º, da Resolução CNJ nº 455/2022). " +
            $"Iniciada a fluência do prazo legal de {calculoPrazo.DiasUteisTotal} (quinze) dias úteis em {calculoPrazo.DataInicioPrazo:dd/MM/yyyy} (Art. 224, § 3º, do CPC/2015), e {feriadosTexto}, " +
            $"o termo fatal perfaz-se somente em {calculoPrazo.DataTermoFatal:dd/MM/yyyy}, restando cabalmente demonstrada a tempestividade do protocolo.";

        // 3. Epígrafe e Qualificação Processual
        var epigrafe =
            $"Processo nº {cnj}\n" +
            $"Classe Processual: {classe}\n" +
            $"Assunto Principal: {assunto}\n" +
            $"Órgão Julgador: {orgao}\n\n" +
            $"{autor.ToUpperInvariant()}, já devidamente qualificada nos autos em epígrafe, por seus advogados que esta subscrevem ({nomeEscritorio} — OAB {oabPrincipal}), " +
            $"nos autos da demanda em que contende com {reu.ToUpperInvariant()}, vem, respeitosamente, à presença de Vossa Excelência, {verboManifestacao}, " +
            $"com fundamento em {tese.FundamentacaoLegal}, pelas razões de fato e de direito a seguir aduzidas.";

        // 4. Síntese Fática e Processual
        var topicoSintese =
            $"Trata-se, na origem, de {classe} versando sobre {assunto}, em trâmite perante {orgao}. " +
            $"Consoante se extrai da evolução processual recente dos autos, sobreveio o seguinte ato/andamento processual:\n\n" +
            $"\"{andamento}\"\n\n" +
            $"Diante do referido cenário processual, impõe-se a imediata aplicação do entendimento consolidado acerca de \"{tese.Titulo}\", " +
            $"pelos fundamentos jurídicos que passam a ser demonstrados de forma analítica.";

        // 5. Mérito e Enquadramento da Tese Jurídica + Precedentes do Juízo
        var sbMerito = new StringBuilder();
        sbMerito.AppendLine($"A controvérsia posta nos autos atrai a incidência direta da tese jurídica atinente a {tese.Titulo} ({tese.AreaDireito}).");
        sbMerito.AppendLine();
        sbMerito.AppendLine($"No plano normativo, a matéria é regida expressamente por: {tese.FundamentacaoLegal}");
        sbMerito.AppendLine();
        sbMerito.AppendLine(tese.ModeloArgumentacao);
        sbMerito.AppendLine();
        sbMerito.AppendLine("Nesse exato sentido, destaca-se o entendimento paradigmático e vinculante sobre a matéria:");
        sbMerito.AppendLine();
        sbMerito.AppendLine($"\"{tese.PrecedenteChave}\"");

        if (precedentesRecentesJuizo is { Count: > 0 })
        {
            sbMerito.AppendLine();
            sbMerito.AppendLine("Corroborando a aderência específica desta tese perante este Douto Juízo/Relatoria, destacam-se os julgados recentes extraídos das bases oficiais:");
            foreach (var julgado in precedentesRecentesJuizo.Take(2))
            {
                sbMerito.AppendLine($"• Processo nº {julgado.NumeroProcesso} ({julgado.Classe} — {julgado.OrgaoJulgador}, publ. em {julgado.DataPublicacao}, Fonte: {julgado.FonteOficial}): \"{julgado.EmentaResumo}\"");
            }
        }

        var topicoMerito = sbMerito.ToString().Trim();

        // 6. Pedidos e Requerimentos Conclusivos
        var topicoPedidos =
            $"ANTE O EXPOSTO, requer a Vossa Excelência:\n\n" +
            $"a) Seja reconhecida a tempestividade e a regularidade formal da presente peça processual, nos termos dos arts. 219 e 224 do CPC/2015;\n" +
            $"b) No mérito, seja integralmente acolhida a fundamentação supra ({tese.Titulo}), com espeque em {tese.FundamentacaoLegal} e na diretriz firmada em {tese.PrecedenteChave};\n" +
            $"c) Por fim, requer que todas as publicações e intimações no Diário de Justiça Eletrônico Nacional (DJEN) sejam realizadas exclusivamente em nome dos patronos constituídos ({nomeEscritorio} — OAB {oabPrincipal}), sob pena de nulidade absoluta (Art. 272, §§ 2º e 5º, do CPC/2015).";

        var dataExtenso = DateTime.UtcNow.AddHours(-3).ToString("dd 'de' MMMM 'de' yyyy", new CultureInfo("pt-BR"));
        var fecho =
            $"Termos em que,\nPede e espera deferimento.\n\n" +
            $"São Paulo/SP, {dataExtenso}.\n\n" +
            $"_______________________________________________________\n" +
            $"{nomeEscritorio.ToUpperInvariant()}\n" +
            $"OAB {oabPrincipal}";

        var textoIntegral =
            $"{enderecamento}\n\n\n" +
            $"{epigrafe}\n\n" +
            $"I. DA TEMPESTIVIDADE E REGULARIDADE FORMAL (ARTS. 219 E 224 DO CPC/2015)\n\n{topicoTempestividade}\n\n" +
            $"II. SÍNTESE PROCESSUAL E DO ATO IMPUGNADO / MANIFESTADO\n\n{topicoSintese}\n\n" +
            $"III. DO DIREITO E DO ENQUADRAMENTO JURÍDICO: {tese.Titulo.ToUpperInvariant()}\n\n{topicoMerito}\n\n" +
            $"IV. DOS PEDIDOS E REQUERIMENTOS CONCLUSIVOS\n\n{topicoPedidos}\n\n" +
            $"{fecho}";

        var htmlWord = ConstruirDocumentoWordAbntForense(
            tituloPeca,
            enderecamento,
            epigrafe,
            topicoTempestividade,
            topicoSintese,
            tese.Titulo,
            topicoMerito,
            tese.PrecedenteChave,
            topicoPedidos,
            fecho,
            nomeEscritorio,
            cnj);

        var slugProcesso = cnj.Replace(".", "").Replace("-", "");
        var nomeArquivo = $"Minuta_{SanitizarNomeArquivo(tituloPeca)}_{slugProcesso}.doc";

        return new MinutaProcessualGerada(
            Id: $"minuta-{Guid.NewGuid():N}",
            TenantId: tenantId,
            NumeroProcesso: cnj,
            TeseId: tese.Id,
            TituloPeca: tituloPeca,
            EnderecamentoOficial: enderecamento,
            EpigrafeQualificacao: epigrafe,
            TopicoTempestividade: topicoTempestividade,
            TopicoSinteseFatica: topicoSintese,
            TopicoMeritoDireito: topicoMerito,
            TopicoPedidosConclusivos: topicoPedidos,
            FechoAssinatura: fecho,
            TextoIntegralFormatado: textoIntegral,
            ConteudoHtmlWordDoc: htmlWord,
            NomeArquivoSugerido: nomeArquivo,
            GeradoEmUtc: DateTime.UtcNow);
    }

    private static (string Enderecamento, string TituloPeca, string VerboManifestacao) DeterminarEnderecamentoETitulo(
        string orgaoJulgador,
        string classe,
        string andamento,
        TeseJuridicaTenant tese)
    {
        var contexto = $"{orgaoJulgador} {classe} {andamento} {tese.Titulo}".ToUpperInvariant();

        if (contexto.Contains("STJ") || contexto.Contains("ARESP") || contexto.Contains("RECURSO ESPECIAL") || contexto.Contains("MIN."))
        {
            return (
                "EXCELENTÍSSIMO(A) SENHOR(A) MINISTRO(A) PRESIDENTE / RELATOR(A) DO EGRÉGIO SUPERIOR TRIBUNAL DE JUSTIÇA — BRASÍLIA/DF",
                "CONTRARRAZÕES / MANIFESTAÇÃO EM AGRAVO EM RECURSO ESPECIAL",
                "apresentar tempestivamente a presente MANIFESTAÇÃO / CONTRARRAZÕES AO AGRAVO EM RECURSO ESPECIAL (ART. 1.042, § 3º, DO CPC/2015)");
        }

        if (contexto.Contains("SISBAJUD") || contexto.Contains("BACENJUD") || contexto.Contains("PENHORA") || contexto.Contains("IMPENHORABILIDADE"))
        {
            return (
                $"EXCELENTÍSSIMO(A) SENHOR(A) DOUTOR(A) JUIZ(A) DE DIREITO / RELATOR(A) PERANTE O(A) {orgaoJulgador.ToUpperInvariant()}",
                "IMPUGNAÇÃO À PENHORA ONLINE (SISBAJUD) COM PEDIDO DE DESBLOQUEIO IMEDIATO",
                "apresentar IMPUGNAÇÃO À CONSTRIÇÃO JUDICIAL VIA SISBAJUD COM PEDIDO DE LEVANTAMENTO IMEDIATO (ART. 833 E ART. 854, § 3º, DO CPC/2015)");
        }

        if (contexto.Contains("CÂMARA") || contexto.Contains("TURMA") || contexto.Contains("AGRAVO DE INSTRUMENTO") || contexto.Contains("DES."))
        {
            return (
                $"EXCELENTÍSSIMO(A) SENHOR(A) DESEMBARGADOR(A) RELATOR(A) DA {orgaoJulgador.ToUpperInvariant()} DO EGRÉGIO TRIBUNAL DE JUSTIÇA",
                "CONTRAMINUTA DE AGRAVO DE INSTRUMENTO / PETIÇÃO INTERCORRENTE",
                "apresentar tempestivamente CONTRAMINUTA / MANIFESTAÇÃO RECURSAL (ART. 1.019, II, DO CPC/2015)");
        }

        return (
            $"EXCELENTÍSSIMO(A) SENHOR(A) DOUTOR(A) JUIZ(A) DE DIREITO DA {orgaoJulgador.ToUpperInvariant()}",
            "PETIÇÃO FUNDAMENTADA DE ENQUADRAMENTO DE TESE JURÍDICA",
            "apresentar a presente MANIFESTAÇÃO FUNDAMENTADA NOS AUTOS");
    }

    private static string ExtrairSiglaTribunal(string orgaoJulgador)
    {
        var upper = orgaoJulgador.ToUpperInvariant();
        if (upper.Contains("STJ")) return "STJ";
        if (upper.Contains("STF")) return "STF";
        if (upper.Contains("TJSC")) return "TJSC";
        if (upper.Contains("TJMS")) return "TJMS";
        if (upper.Contains("TJCE")) return "TJCE";
        return "TJSP";
    }

    private static string SanitizarNomeArquivo(string texto)
    {
        var limpo = new string(texto
            .Where(c => char.IsLetterOrDigit(c) || c == ' ')
            .ToArray())
            .Trim()
            .Replace(" ", "_");
        return limpo.Length > 32 ? limpo[..32] : limpo;
    }

    private static string ConstruirDocumentoWordAbntForense(
        string tituloPeca,
        string enderecamento,
        string epigrafe,
        string topicoTempestividade,
        string topicoSintese,
        string tituloTese,
        string topicoMerito,
        string jurisprudenciaParadigma,
        string topicoPedidos,
        string fecho,
        string nomeEscritorio,
        string cnj)
    {
        static string H(string s) => WebUtility.HtmlEncode(s).Replace("\n", "<br/>");

        return $@"<html xmlns:o='urn:schemas-microsoft-com:office:office' xmlns:w='urn:schemas-microsoft-com:office:word' xmlns='http://www.w3.org/TR/REC-html40'>
<head>
<meta charset='utf-8'>
<meta http-equiv='Content-Type' content='text/html; charset=utf-8' />
<!-- Format: application/msword ABNT Forensic Template -->
<title>{WebUtility.HtmlEncode(tituloPeca)} - {WebUtility.HtmlEncode(cnj)}</title>
<style>
  @page WordSection1 {{
    size: 21.0cm 29.7cm;
    margin: 3.0cm 2.0cm 2.0cm 3.0cm;
    mso-header-margin: 1.25cm;
    mso-footer-margin: 1.25cm;
  }}
  div.WordSection1 {{ page: WordSection1; }}
  body {{
    font-family: 'Times New Roman', Georgia, serif;
    font-size: 12pt;
    line-height: 1.5;
    color: #000000;
    text-align: justify;
  }}
  .header-firm {{
    text-align: center;
    font-size: 10pt;
    color: #333333;
    border-bottom: 1px solid #666666;
    padding-bottom: 6pt;
    margin-bottom: 24pt;
  }}
  .enderecamento {{
    font-weight: bold;
    text-transform: uppercase;
    text-align: justify;
    margin-bottom: 36pt;
  }}
  .epigrafe {{
    margin-bottom: 24pt;
    text-align: justify;
  }}
  h2.section-title {{
    font-size: 12pt;
    font-weight: bold;
    text-transform: uppercase;
    margin-top: 18pt;
    margin-bottom: 10pt;
  }}
  p.body-paragraph {{
    text-indent: 1.25cm;
    margin-top: 0pt;
    margin-bottom: 10pt;
    text-align: justify;
  }}
  div.citacao-jurisprudencia {{
    margin-left: 4.0cm;
    margin-top: 10pt;
    margin-bottom: 12pt;
    font-size: 10.5pt;
    line-height: 1.15;
    font-style: italic;
    border-left: 2pt solid #444444;
    padding-left: 10pt;
    text-align: justify;
  }}
  .fecho {{
    margin-top: 30pt;
    text-align: center;
  }}
</style>
</head>
<body>
<div class='WordSection1'>
  <div class='header-firm'>
    <strong>{WebUtility.HtmlEncode(nomeEscritorio.ToUpperInvariant())}</strong><br/>
    Peça Processual Gerada pelo Motor de Inteligência Jurídica — Autos nº {WebUtility.HtmlEncode(cnj)}
  </div>

  <div class='enderecamento'>{H(enderecamento)}</div>

  <div class='epigrafe'>{H(epigrafe)}</div>

  <h2 class='section-title'>I. DA TEMPESTIVIDADE E REGULARIDADE FORMAL (ARTS. 219 E 224 DO CPC/2015)</h2>
  <p class='body-paragraph'>{H(topicoTempestividade)}</p>

  <h2 class='section-title'>II. SÍNTESE PROCESSUAL E DO ATO JUDICIAL IMPUGNADO / MANIFESTADO</h2>
  <p class='body-paragraph'>{H(topicoSintese)}</p>

  <h2 class='section-title'>III. DO DIREITO E DO ENQUADRAMENTO DA TESE: {WebUtility.HtmlEncode(tituloTese.ToUpperInvariant())}</h2>
  <p class='body-paragraph'>{H(topicoMerito)}</p>
  <div class='citacao-jurisprudencia'>
    <strong>Jurisprudência / Precedente Paradigma Vinculante:</strong><br/>
    {H(jurisprudenciaParadigma)}
  </div>

  <h2 class='section-title'>IV. DOS PEDIDOS E REQUERIMENTOS CONCLUSIVOS</h2>
  <p class='body-paragraph'>{H(topicoPedidos)}</p>

  <div class='fecho'>{H(fecho)}</div>
</div>
</body>
</html>";
    }
}
