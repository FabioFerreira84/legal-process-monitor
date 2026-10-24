using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace LegalProcessMonitor.Infrastructure.Connectors.Esaj;

public class EsajOabScraper : IEsajOabScraper
{
    private readonly IDjenComunicacaoConnector _djenConnector;
    private readonly ILogger<EsajOabScraper> _logger;

    public EsajOabScraper(IDjenComunicacaoConnector djenConnector, ILogger<EsajOabScraper> logger)
    {
        _djenConnector = djenConnector;
        _logger = logger;
    }

    public async Task<AdvogadoCarteira> ConsultarProcessosPorOabAsync(string numeroOab, CancellationToken ct = default)
    {
        var oabLimpa = Regex.Replace(numeroOab ?? "", @"[^a-zA-Z0-9]", "").ToUpperInvariant();
        var numDigitos = Regex.Replace(oabLimpa, @"\D", "");
        var uf = Regex.Replace(oabLimpa, @"[^A-Z]", "");
        if (string.IsNullOrWhiteSpace(uf)) uf = "SP";

        var carteira = new AdvogadoCarteira
        {
            NumeroOab = $"{numDigitos}{uf}",
            Tribunal = uf == "SP" ? "DJEN/CNJ Nacional + TJSP (e-SAJ)" : $"DJEN/CNJ Nacional ({uf})"
        };

        if (string.IsNullOrWhiteSpace(numDigitos))
        {
            carteira.AvisosOuErros.Add("Número de inscrição OAB inválido.");
            return carteira;
        }

        // 1. Consulta Oficial na API Nacional do DJEN / Comunica PJe (CNJ)
        await _djenConnector.EnriquecerCarteiraPorOabAsync(carteira, numDigitos, uf, ct);

        // 2. Se for OAB/SP, complementa com varredura direta no e-SAJ TJSP (1º e 2º Grau)
        if (uf == "SP")
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
                    Timeout = TimeSpan.FromSeconds(20)
                };
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

                var parametroEsaj = $"{numDigitos}{uf}";

                // 1º Grau (cpopg)
                await client.GetAsync("https://esaj.tjsp.jus.br/cpopg/open.do", ct);
                var url1g = $"https://esaj.tjsp.jus.br/cpopg/search.do?conversationId=&cbPesquisa=NUMOAB&dadosConsulta.valorConsulta={Uri.EscapeDataString(parametroEsaj)}&cdForo=-1";
                var html1g = await client.GetStringAsync(url1g, ct);
                ExtrairProcessos1Grau(html1g, carteira);
                carteira.FontesConsultadas.Add("e-SAJ TJSP 1º Grau");

                // 2º Grau (cposg)
                await client.GetAsync("https://esaj.tjsp.jus.br/cposg/open.do", ct);
                var url2g = $"https://esaj.tjsp.jus.br/cposg/search.do?conversationId=&paginaConsulta=0&cbPesquisa=NUMOAB&tipoNuProcesso=UNIFICADO&dePesquisaNuUnificado=&dePesquisa={Uri.EscapeDataString(parametroEsaj)}";
                var html2g = await client.GetStringAsync(url2g, ct);
                ExtrairProcessos2Grau(html2g, carteira);
                carteira.FontesConsultadas.Add("e-SAJ TJSP 2º Grau");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao consultar portal e-SAJ TJSP para a OAB {Oab}", carteira.NumeroOab);
                carteira.AvisosOuErros.Add($"Portal e-SAJ TJSP indisponível ou bloqueou a consulta automatizada ({ex.GetType().Name}). Exibindo registros oficiais obtidos via DJEN/CNJ.");
            }
        }

        return carteira;
    }

    public static void ExtrairProcessos1Grau(string html1g, AdvogadoCarteira carteira)
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
                Tribunal = "TJSP",
                Grau = 1,
                Classe = !string.IsNullOrWhiteSpace(assunto) ? $"{classe} ({assunto})" : classe,
                Vara = dataVara,
                Papel = "Advogado(a) habilitado(a)",
                DataRecebimento = dataVara.Contains('-') ? dataVara.Split('-')[0].Trim() : "",
                UrlOrigem = $"https://esaj.tjsp.jus.br/cpopg/search.do?cbPesquisa=NUMPROC&tipoNuProcesso=UNIFICADO&dePesquisaNuUnificado={cnjFormatado}",
                FonteDados = "e-SAJ TJSP 1º Grau"
            });
        }
    }

    public static void ExtrairProcessos2Grau(string html2g, AdvogadoCarteira carteira)
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
            var dataLocal = matchDataLocal.Success ? WebUtility.HtmlDecode(matchDataLocal.Groups[1].Value).Trim() : "2º Grau TJSP";

            carteira.Processos.Add(new ProcessoResumo
            {
                NumeroCnjFormatado = cnjFormatado,
                NumeroCnjLimpo = cnjLimpo,
                Tribunal = "TJSP",
                Grau = 2,
                Classe = !string.IsNullOrWhiteSpace(assunto) ? $"{classe} ({assunto})" : classe,
                Vara = dataLocal,
                Papel = "Advogado(a) / Patrono recursal",
                DataRecebimento = dataLocal.Contains('-') ? dataLocal.Split('-')[0].Trim() : "",
                UrlOrigem = $"https://esaj.tjsp.jus.br/cposg/search.do?cbPesquisa=NUMPROC&tipoNuProcesso=UNIFICADO&dePesquisaNuUnificado={cnjFormatado}",
                FonteDados = "e-SAJ TJSP 2º Grau"
            });
        }
    }
}
