using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;
using LegalProcessMonitor.Infrastructure.Connectors.Rj;
using Microsoft.Extensions.Logging;

namespace LegalProcessMonitor.Infrastructure.Connectors.Esaj;

public class EsajOabScraper : IEsajOabScraper
{
    private readonly IDjenComunicacaoConnector _djenConnector;
    private readonly IRjTribunaisScraper _rjScraper;
    private readonly ILogger<EsajOabScraper> _logger;

    public EsajOabScraper(
        IDjenComunicacaoConnector djenConnector,
        IRjTribunaisScraper rjScraper,
        ILogger<EsajOabScraper> logger)
    {
        _djenConnector = djenConnector;
        _rjScraper = rjScraper;
        _logger = logger;
    }

    private static readonly IReadOnlyDictionary<string, (string SiglaTribunal, string BaseHost, string CpoPgPath, string CpoSgPath)> PortaisEsajPorUf =
        new Dictionary<string, (string SiglaTribunal, string BaseHost, string CpoPgPath, string CpoSgPath)>(StringComparer.OrdinalIgnoreCase)
        {
            ["SP"] = ("TJSP", "https://esaj.tjsp.jus.br", "cpopg", "cposg"),
            ["SC"] = ("TJSC", "https://esaj.tjsc.jus.br", "cpopg", "cposg"),
            ["MS"] = ("TJMS", "https://esaj.tjms.jus.br", "cpopg5", "cposg5"),
            ["CE"] = ("TJCE", "https://esaj.tjce.jus.br", "cpopg", "cposg"),
            ["AM"] = ("TJAM", "https://consultasaj.tjam.jus.br", "cpopg", "cposg"),
            ["AL"] = ("TJAL", "https://www2.tjal.jus.br", "cpopg", "cposg5"),
            ["AC"] = ("TJAC", "https://esaj.tjac.jus.br", "cpopg", "cposg5")
        };

    public async Task<AdvogadoCarteira> ConsultarProcessosPorOabAsync(string numeroOab, CancellationToken ct = default)
    {
        var oabLimpa = Regex.Replace(numeroOab ?? "", @"[^a-zA-Z0-9]", "").ToUpperInvariant();
        var numDigitos = Regex.Replace(oabLimpa, @"\D", "");
        var uf = Regex.Replace(oabLimpa, @"[^A-Z]", "");
        if (string.IsNullOrWhiteSpace(uf)) uf = "SP";

        var temEsajEstadual = PortaisEsajPorUf.TryGetValue(uf, out var portalEsaj);

        var carteira = new AdvogadoCarteira
        {
            NumeroOab = $"{numDigitos}{uf}",
            Tribunal = temEsajEstadual
                ? $"DJEN/CNJ Nacional + {portalEsaj.SiglaTribunal} (e-SAJ)"
                : $"DJEN/CNJ Nacional ({uf})"
        };

        if (string.IsNullOrWhiteSpace(numDigitos))
        {
            carteira.AvisosOuErros.Add("Número de inscrição OAB inválido.");
            return carteira;
        }

        // 1. Consulta Oficial na API Nacional do DJEN / Comunica PJe (CNJ)
        await _djenConnector.EnriquecerCarteiraPorOabAsync(carteira, numDigitos, uf, ct);

        // 2. Se a UF for RJ, aciona o scraper multi-tribunal do Rio de Janeiro (TJRJ + TRF2)
        if (string.Equals(uf, "RJ", StringComparison.OrdinalIgnoreCase))
        {
            carteira.Tribunal = "TJRJ + TRF2 + TRT1 (Ecossistema Fluminense) + DJEN";
            await _rjScraper.EnriquecerCarteiraRjAsync(carteira, numDigitos, ct);
        }

        // 3. Se a UF possuir portal e-SAJ mapeado (SP, SC, MS, CE, AM, AL, AC), complementa com varredura direta (1º e 2º Grau)
        if (temEsajEstadual)
        {
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
                    Timeout = TimeSpan.FromSeconds(18)
                };
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

                var parametroEsaj = $"{numDigitos}{uf}";

                // 1º Grau (cpopg / cpopg5)
                await client.GetAsync($"{portalEsaj.BaseHost}/{portalEsaj.CpoPgPath}/open.do", ct);
                var url1g = $"{portalEsaj.BaseHost}/{portalEsaj.CpoPgPath}/search.do?conversationId=&cbPesquisa=NUMOAB&dadosConsulta.valorConsulta={Uri.EscapeDataString(parametroEsaj)}&cdForo=-1";
                var html1g = await client.GetStringAsync(url1g, ct);
                ExtrairProcessos1Grau(html1g, carteira, portalEsaj.SiglaTribunal, portalEsaj.BaseHost, portalEsaj.CpoPgPath);
                carteira.FontesConsultadas.Add($"e-SAJ {portalEsaj.SiglaTribunal} 1º Grau");

                // 2º Grau (cposg / cposg5)
                await client.GetAsync($"{portalEsaj.BaseHost}/{portalEsaj.CpoSgPath}/open.do", ct);
                var url2g = $"{portalEsaj.BaseHost}/{portalEsaj.CpoSgPath}/search.do?conversationId=&paginaConsulta=0&cbPesquisa=NUMOAB&tipoNuProcesso=UNIFICADO&dePesquisaNuUnificado=&dePesquisa={Uri.EscapeDataString(parametroEsaj)}";
                var html2g = await client.GetStringAsync(url2g, ct);
                ExtrairProcessos2Grau(html2g, carteira, portalEsaj.SiglaTribunal, portalEsaj.BaseHost, portalEsaj.CpoSgPath);
                carteira.FontesConsultadas.Add($"e-SAJ {portalEsaj.SiglaTribunal} 2º Grau");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao consultar portal e-SAJ {Tribunal} para a OAB {Oab}", portalEsaj.SiglaTribunal, carteira.NumeroOab);
                carteira.AvisosOuErros.Add($"Portal e-SAJ {portalEsaj.SiglaTribunal} indisponível ou bloqueou a consulta automatizada ({ex.GetType().Name}). Exibindo registros oficiais obtidos via DJEN/CNJ.");
            }
        }

        return carteira;
    }

    public static void ExtrairProcessos1Grau(
        string html1g,
        AdvogadoCarteira carteira,
        string siglaTribunal = "TJSP",
        string baseHost = "https://esaj.tjsp.jus.br",
        string cpoPgPath = "cpopg")
    {
        if (string.IsNullOrWhiteSpace(html1g)) return;

        var regexLi = @"(?s)<li[^>]*>\s*<div id=""divProcesso[^""]*""[^>]*>(.*?)</li>";
        var matches = Regex.Matches(html1g, regexLi, RegexOptions.IgnoreCase);

        foreach (Match m in matches)
        {
            var block = m.Groups[1].Value;
            var matchCnj = Regex.Match(block, @"<a[^>]*class=""linkProcesso""[^>]*>\s*([0-9\.\-]+)\s*</a>", RegexOptions.IgnoreCase);
            if (!matchCnj.Success) continue;

            var cnjFormatado = matchCnj.Groups[1].Value.Trim();
            var cnjLimpo = Regex.Replace(cnjFormatado, @"\D", "");

            if (string.IsNullOrWhiteSpace(carteira.NomeAdvogado))
            {
                var matchAdv = Regex.Match(block, @"class=""unj-base-alt nomeParte""[^>]*>\s*([^<]+)\s*</div>", RegexOptions.IgnoreCase);
                if (matchAdv.Success && !string.IsNullOrWhiteSpace(matchAdv.Groups[1].Value))
                {
                    carteira.NomeAdvogado = WebUtility.HtmlDecode(matchAdv.Groups[1].Value).Trim();
                }
            }

            if (carteira.Processos.Exists(p => p.NumeroCnjLimpo == cnjLimpo))
                continue;

            var matchClasse = Regex.Match(block, @"class=""classeProcesso""[^>]*>\s*([^<]+)\s*</div>", RegexOptions.IgnoreCase);
            var matchAssunto = Regex.Match(block, @"class=""assuntoPrincipalProcesso""[^>]*>\s*([^<]+)\s*</div>", RegexOptions.IgnoreCase);
            var matchDataVara = Regex.Match(block, @"class=""dataLocalDistribuicaoProcesso""[^>]*>\s*([^<]+)\s*</div>", RegexOptions.IgnoreCase);

            var classe = matchClasse.Success ? WebUtility.HtmlDecode(matchClasse.Groups[1].Value).Trim() : "Processo Judicial";
            var assunto = matchAssunto.Success ? WebUtility.HtmlDecode(matchAssunto.Groups[1].Value).Trim() : "";
            var dataVara = matchDataVara.Success ? WebUtility.HtmlDecode(matchDataVara.Groups[1].Value).Trim() : "";

            carteira.Processos.Add(new ProcessoResumo
            {
                NumeroCnjFormatado = cnjFormatado,
                NumeroCnjLimpo = cnjLimpo,
                Tribunal = siglaTribunal,
                Grau = 1,
                Classe = !string.IsNullOrWhiteSpace(assunto) ? $"{classe} ({assunto})" : classe,
                Vara = dataVara,
                Papel = "Advogado(a) habilitado(a)",
                DataRecebimento = dataVara.Contains('-') ? dataVara.Split('-')[0].Trim() : "",
                UrlOrigem = $"{baseHost}/{cpoPgPath}/search.do?cbPesquisa=NUMPROC&tipoNuProcesso=UNIFICADO&dePesquisaNuUnificado={cnjFormatado}",
                FonteDados = $"e-SAJ {siglaTribunal} 1º Grau"
            });
        }
    }

    public static void ExtrairProcessos2Grau(
        string html2g,
        AdvogadoCarteira carteira,
        string siglaTribunal = "TJSP",
        string baseHost = "https://esaj.tjsp.jus.br",
        string cpoSgPath = "cposg")
    {
        if (string.IsNullOrWhiteSpace(html2g)) return;

        var regexLi = @"(?s)<li[^>]*>\s*<div id=""divProcesso[^""]*""[^>]*>(.*?)</li>";
        var matches = Regex.Matches(html2g, regexLi, RegexOptions.IgnoreCase);

        foreach (Match m in matches)
        {
            var block = m.Groups[1].Value;
            var matchCnj = Regex.Match(block, @"<a[^>]*class=""linkProcesso""[^>]*>\s*([0-9\.\-]+)\s*</a>", RegexOptions.IgnoreCase);
            if (!matchCnj.Success) continue;

            var cnjFormatado = matchCnj.Groups[1].Value.Trim();
            var cnjLimpo = Regex.Replace(cnjFormatado, @"\D", "");

            if (carteira.Processos.Exists(p => p.NumeroCnjLimpo == cnjLimpo))
                continue;

            var matchClasse = Regex.Match(block, @"class=""classeProcesso""[^>]*>\s*([^<]+)\s*</div>", RegexOptions.IgnoreCase);
            var matchAssunto = Regex.Match(block, @"class=""assuntoPrincipalProcesso""[^>]*>\s*([^<]+)\s*</div>", RegexOptions.IgnoreCase);
            var matchDataLocal = Regex.Match(block, @"class=""dataLocalDistribuicao(?:Processo)?""[^>]*>\s*([^<]+)\s*</div>", RegexOptions.IgnoreCase);

            var classe = matchClasse.Success ? WebUtility.HtmlDecode(matchClasse.Groups[1].Value).Trim() : "Recurso de 2º Grau";
            var assunto = matchAssunto.Success ? WebUtility.HtmlDecode(matchAssunto.Groups[1].Value).Trim() : "";
            var dataLocal = matchDataLocal.Success ? WebUtility.HtmlDecode(matchDataLocal.Groups[1].Value).Trim() : $"2º Grau {siglaTribunal}";

            carteira.Processos.Add(new ProcessoResumo
            {
                NumeroCnjFormatado = cnjFormatado,
                NumeroCnjLimpo = cnjLimpo,
                Tribunal = siglaTribunal,
                Grau = 2,
                Classe = !string.IsNullOrWhiteSpace(assunto) ? $"{classe} ({assunto})" : classe,
                Vara = dataLocal,
                Papel = "Advogado(a) / Patrono recursal",
                DataRecebimento = dataLocal.Contains('-') ? dataLocal.Split('-')[0].Trim() : "",
                UrlOrigem = $"{baseHost}/{cpoSgPath}/search.do?cbPesquisa=NUMPROC&tipoNuProcesso=UNIFICADO&dePesquisaNuUnificado={cnjFormatado}",
                FonteDados = $"e-SAJ {siglaTribunal} 2º Grau"
            });
        }
    }
}
