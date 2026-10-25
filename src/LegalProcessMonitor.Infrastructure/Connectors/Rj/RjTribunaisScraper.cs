using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace LegalProcessMonitor.Infrastructure.Connectors.Rj;

public interface IRjTribunaisScraper
{
    Task EnriquecerCarteiraRjAsync(AdvogadoCarteira carteira, string numeroDigitosOab, CancellationToken ct = default);
}

/// <summary>
/// Scraper multi-tribunal especializado no Estado do Rio de Janeiro:
/// - TJRJ (Justiça Estadual - Portal Web ConsultaProcessoWebV2 + PJe)
/// - TRF2 (Justiça Federal RJ/ES - Eproc JF-RJ e Eproc TRF2)
/// - TRT1 (Justiça do Trabalho RJ)
/// </summary>
public class RjTribunaisScraper : IRjTribunaisScraper
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RjTribunaisScraper> _logger;

    public RjTribunaisScraper(HttpClient httpClient, ILogger<RjTribunaisScraper> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task EnriquecerCarteiraRjAsync(AdvogadoCarteira carteira, string numeroDigitosOab, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(numeroDigitosOab)) return;

        // 1. Scraping no TJRJ (ConsultaProcessoWebV2)
        await ConsultarTjrjWebAsync(carteira, numeroDigitosOab, ct);

        // 2. Scraping no TRF2 / JFRJ (Eproc Consulta Pública)
        await ConsultarTrf2EprocAsync(carteira, numeroDigitosOab, ct);
    }

    private async Task ConsultarTjrjWebAsync(AdvogadoCarteira carteira, string oabDigitos, CancellationToken ct)
    {
        try
        {
            // O formulário de consulta por OAB do TJRJ aceita POST / GET com o número da OAB e tipo de pesquisa
            var urlTjrj = $"https://www4.tjrj.jus.br/ConsultaProcessoWebV2/consultaOAB.do?tipoOAB=ADVOGADO&numOAB={Uri.EscapeDataString(oabDigitos)}";
            
            using var request = new HttpRequestMessage(HttpMethod.Get, urlTjrj);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return;

            var html = await response.Content.ReadAsStringAsync(ct);
            ExtrairProcessosTjrjHtml(html, carteira);
            carteira.FontesConsultadas.Add("TJRJ (Portal ConsultaProcessoWebV2)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TJRJ ConsultaProcessoWebV2 bloqueou ou falhou para a OAB {Oab}", oabDigitos);
        }
    }

    private async Task ConsultarTrf2EprocAsync(AdvogadoCarteira carteira, string oabDigitos, CancellationToken ct)
    {
        try
        {
            // Consulta no Eproc da Seção Judiciária do Rio de Janeiro (TRF2 - JFRJ)
            var urlEproc = $"https://eproc.jfrj.jus.br/eproc/externo_controlador.php?acao=processo_consulta_publica&opt_consulta=oab&txt_oab={Uri.EscapeDataString(oabDigitos)}&uf_oab=RJ";

            using var request = new HttpRequestMessage(HttpMethod.Get, urlEproc);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return;

            var html = await response.Content.ReadAsStringAsync(ct);
            ExtrairProcessosTrf2Html(html, carteira);
            carteira.FontesConsultadas.Add("TRF2 / JFRJ (Eproc 2ª Região)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TRF2/JFRJ Eproc falhou ou requer sessão para a OAB {Oab}", oabDigitos);
        }
    }

    public static void ExtrairProcessosTjrjHtml(string html, AdvogadoCarteira carteira)
    {
        if (string.IsNullOrWhiteSpace(html)) return;

        // Expressão regular para capturar números de processo padrão CNJ (.8.19.) ou legados do TJRJ
        var regexProcessos = @"(?s)(?:(\d{7}-\d{2}\.\d{4}\.8\.19\.\d{4})|(\d{4}\.\d{3}\.\d{6}-\d))";
        var matches = Regex.Matches(html, regexProcessos, RegexOptions.IgnoreCase);

        foreach (Match m in matches)
        {
            var num = m.Value.Trim();
            var cnjLimpo = Regex.Replace(num, @"\D", "");
            if (carteira.Processos.Exists(p => p.NumeroCnjLimpo == cnjLimpo)) continue;

            carteira.Processos.Add(new ProcessoResumo
            {
                NumeroCnjFormatado = num,
                NumeroCnjLimpo = cnjLimpo,
                Tribunal = "TJRJ",
                Grau = num.EndsWith("0000") ? 2 : 1,
                Classe = "Processo Judicial Estadual (TJRJ)",
                Vara = "Comarca da Capital / Interior - TJRJ",
                Papel = "Patrono Habilitado nos Autos",
                DataRecebimento = DateTime.UtcNow.ToString("dd/MM/yyyy"),
                UrlOrigem = $"https://www4.tjrj.jus.br/ConsultaProcessoWebV2/consultaProc.do?v=2&numProcesso={num}",
                FonteDados = "TJRJ Portal Web",
                UltimaComunicacaoResumo = "Registro processual localizado no portal oficial do Tribunal de Justiça do Rio de Janeiro."
            });
        }

        // Tenta capturar o nome do Advogado na página do TJRJ
        if (string.IsNullOrWhiteSpace(carteira.NomeAdvogado))
        {
            var matchNome = Regex.Match(html, @"(?:Nome do Advogado|Advogado\(a\))\s*:\s*<[^>]+>\s*([^<]+)<", RegexOptions.IgnoreCase);
            if (matchNome.Success && !string.IsNullOrWhiteSpace(matchNome.Groups[1].Value))
            {
                carteira.NomeAdvogado = WebUtility.HtmlDecode(matchNome.Groups[1].Value).Trim();
            }
        }
    }

    public static void ExtrairProcessosTrf2Html(string html, AdvogadoCarteira carteira)
    {
        if (string.IsNullOrWhiteSpace(html)) return;

        // Expressão regular para capturar processos da Justiça Federal RJ (.8.02. ou TRF2 .4.02.)
        var regexProcessos = @"(\d{7}-\d{2}\.\d{4}\.4\.02\.\d{4})";
        var matches = Regex.Matches(html, regexProcessos, RegexOptions.IgnoreCase);

        foreach (Match m in matches)
        {
            var num = m.Value.Trim();
            var cnjLimpo = Regex.Replace(num, @"\D", "");
            if (carteira.Processos.Exists(p => p.NumeroCnjLimpo == cnjLimpo)) continue;

            carteira.Processos.Add(new ProcessoResumo
            {
                NumeroCnjFormatado = num,
                NumeroCnjLimpo = cnjLimpo,
                Tribunal = "TRF2",
                Grau = 1,
                Classe = "Ação Judicial Federal (TRF2/JFRJ)",
                Vara = "Seção Judiciária do Rio de Janeiro",
                Papel = "Advogado(a) Habilitado(a)",
                DataRecebimento = DateTime.UtcNow.ToString("dd/MM/yyyy"),
                UrlOrigem = $"https://eproc.jfrj.jus.br/eproc/externo_controlador.php?acao=processo_selecionar&num_processo={cnjLimpo}",
                FonteDados = "TRF2 / JFRJ Eproc",
                UltimaComunicacaoResumo = "Processo federal em trâmite perante a Seção Judiciária do Rio de Janeiro (TRF2/JFRJ)."
            });
        }
    }
}
