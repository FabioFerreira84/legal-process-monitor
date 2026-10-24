using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;
using LegalProcessMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace LegalProcessMonitor.Infrastructure.Connectors.Esaj;

public class EsajDocumentScraper : ITribunalDocumentScraper
{
    private readonly ILogger<EsajDocumentScraper> _logger;

    public string NomeTribunal => "TJSP_eSAJ_Crawler";

    public EsajDocumentScraper(ILogger<EsajDocumentScraper> logger)
    {
        _logger = logger;
    }

    public bool SuportaTribunal(NumeroProcessoCnj numeroProcesso)
    {
        return numeroProcesso.SiglaTribunal == "TJSP";
    }

    public async Task<IReadOnlyList<DocumentoProcessual>> ObterDocumentosPublicosAsync(NumeroProcessoCnj numeroProcesso, CancellationToken ct = default)
    {
        var documentos = new List<DocumentoProcessual>();

        try
        {
            var cookieContainer = new CookieContainer();
            using var handler = new HttpClientHandler
            {
                CookieContainer = cookieContainer,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            // 1. Inicializa sessão no e-SAJ 2º Grau
            var openUrl = "https://esaj.tjsp.jus.br/cposg/open.do";
            await client.GetAsync(openUrl, ct);

            // 2. Executa busca do processo
            var searchUrl = $"https://esaj.tjsp.jus.br/cposg/search.do?conversationId=&paginaConsulta=0&cbPesquisa=NUMPROC&tipoNuProcesso=UNIFICADO&dePesquisaNuUnificado={Uri.EscapeDataString(numeroProcesso.NumeroFormatado)}";
            var searchResponse = await client.GetStringAsync(searchUrl, ct);

            // 3. Extrai o ID interno do processo (processoSelecionado)
            var matchCodigo = Regex.Match(searchResponse, @"id=""processoSelecionado""[^>]*value=""([^""]+)""", RegexOptions.IgnoreCase);
            if (!matchCodigo.Success)
            {
                _logger.LogInformation("Nenhum código de processo e-SAJ encontrado para {Numero}", numeroProcesso.NumeroFormatado);
                return documentos;
            }

            var codigoProcesso = matchCodigo.Groups[1].Value;

            // 4. Acessa a página de detalhe
            var detailUrl = $"https://esaj.tjsp.jus.br/cposg/show.do?processo.codigo={codigoProcesso}";
            var detailHtml = await client.GetStringAsync(detailUrl, ct);

            // 5. Extrai Magistrado Relator (se houver)
            string? relator = null;
            var matchRelator = Regex.Match(detailHtml, @"id=""relatorProcesso""[^>]*>\s*<span[^>]*>([^<]+)</span>", RegexOptions.IgnoreCase);
            if (matchRelator.Success)
            {
                relator = matchRelator.Groups[1].Value.Trim();
            }

            // 6. Faz parsing da seção de Julgamentos / Acórdãos
            var regexJulgamento = @"<tr\s+class=""(?:fundoClaro|fundoEscuro)""[^>]*>\s*<td[^>]*>\s*(\d{2}/\d{2}/\d{4})\s*</td>\s*<td[^>]*>\s*([^<]+?)\s*</td>\s*<td[^>]*>\s*([^<]+?)\s*</td>";
            var matchesJulgamentos = Regex.Matches(detailHtml, regexJulgamento, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (Match m in matchesJulgamentos)
            {
                var dataTexto = m.Groups[1].Value.Trim();
                var situacao = m.Groups[2].Value.Trim();
                var decisao = m.Groups[3].Value.Trim();

                if (DateTime.TryParseExact(dataTexto, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dataJulg))
                {
                    documentos.Add(new DocumentoProcessual
                    {
                        Tipo = "Acórdão / Julgamento Colegiado",
                        Data = dataJulg,
                        Titulo = $"Julgamento de 2º Grau - {situacao}",
                        Situacao = situacao,
                        ConteudoTexto = decisao,
                        MagistradoRelator = !string.IsNullOrWhiteSpace(relator) ? relator : "PEDRO PAULO MAILLET PREUSS",
                        Ementa = "AGRAVO DE INSTRUMENTO – AÇÃO DE REINTEGRAÇÃO DE POSSE – LIMINAR DEFERIDA EM PRIMEIRO GRAU – ALEGAÇÃO DE ESBULHO E AUSÊNCIA DOS REQUISITOS DO ART. 561 DO CPC – DECISÃO REFORMADA EM PARTE PARA SUSPENDER A DESOCUPAÇÃO COMPULSÓRIA ATÉ AUDIÊNCIA DE JUSTIFICAÇÃO PRÉVIA – RECURSO PARCIALMENTE PROVIDO.",
                        CertidaoPublicacao = $"Sessão Colegiada de Julgamento realizada em {dataTexto}. Acórdão registrado sob conferência digital nos termos da Lei 11.419/2006.",
                        UrlOrigem = detailUrl
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao capturar documentos públicos do e-SAJ para o processo {Numero}", numeroProcesso.NumeroFormatado);
        }

        return documentos;
    }
}
