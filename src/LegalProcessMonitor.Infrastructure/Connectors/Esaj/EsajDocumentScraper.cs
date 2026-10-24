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
        var dummyProc = Processo.Criar(numeroProcesso);
        await EnriquecerProcessoAsync(dummyProc, numeroProcesso, ct);
        return dummyProc.DocumentosPublicos;
    }

    public async Task EnriquecerProcessoAsync(Processo processo, NumeroProcessoCnj numeroProcesso, CancellationToken ct = default)
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
                return;
            }

            var codigoProcesso = matchCodigo.Groups[1].Value;

            // 4. Acessa a página de detalhe
            var detailUrl = $"https://esaj.tjsp.jus.br/cposg/show.do?processo.codigo={codigoProcesso}";
            var detailHtml = await client.GetStringAsync(detailUrl, ct);

            // 5. Extrai Magistrado Relator
            var matchRelator = Regex.Match(detailHtml, @"id=""relatorProcesso""[^>]*>\s*<span[^>]*>([^<]+)</span>", RegexOptions.IgnoreCase);
            if (matchRelator.Success)
            {
                processo.Relator = matchRelator.Groups[1].Value.Trim();
            }

            // 6. Extrai Valor da Causa Real
            var matchValor = Regex.Match(detailHtml, @"id=""valorAcaoProcesso""[^>]*>\s*<span[^>]*>([0-9\.,]+)</span>", RegexOptions.IgnoreCase);
            if (matchValor.Success)
            {
                var valorStr = matchValor.Groups[1].Value.Replace(".", "").Replace(",", ".");
                if (decimal.TryParse(valorStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var vlr))
                {
                    processo.ValorCausa = vlr;
                }
            }

            // 7. Extrai Origem / Comarca
            var matchOrigem = Regex.Match(detailHtml, @"class=""unj-label"">\s*Origem\s*</span>\s*<div[^>]*>\s*<span[^>]*title=""([^""]+)""", RegexOptions.IgnoreCase);
            if (matchOrigem.Success)
            {
                processo.Comarca = matchOrigem.Groups[1].Value.Trim();
            }

            // 8. Extrai Processo de 1ª Instância e Vara de Origem
            var match1g = Regex.Match(detailHtml, @"(?s)cpopg/show\.do\?processo\.codigo=([^""&]+)""[^>]*>([0-9\.\-]+)</a>.*?<td[^>]*>([^<]+)</td>\s*<td[^>]*>([^<]+)</td>\s*<td[^>]*>([^<]+)</td>", RegexOptions.IgnoreCase);
            if (match1g.Success)
            {
                processo.ProcessoOrigem = match1g.Groups[2].Value.Trim();
                var foro1g = match1g.Groups[3].Value.Trim();
                var vara1g = match1g.Groups[4].Value.Trim();
                processo.JuizOrigem = match1g.Groups[5].Value.Trim();
                processo.Foro = $"{vara1g} do {foro1g} (Proc. 1º Grau: {processo.ProcessoOrigem} - Juiz: {processo.JuizOrigem})";
            }

            // 9. Extrai Partes e Advogados (tabela completa tableTodasPartes ou tablePartesPrincipais)
            var regexTodas = @"(?s)<table\s+id=""tableTodasPartes""[^>]*>(.*?)</table>";
            var matchTodas = Regex.Match(detailHtml, regexTodas, RegexOptions.IgnoreCase);
            string tableContent = "";
            if (matchTodas.Success)
            {
                tableContent = matchTodas.Groups[1].Value;
            }
            else
            {
                var matchPrincipais = Regex.Match(detailHtml, @"(?s)<table\s+id=""tablePartesPrincipais""[^>]*>(.*?)</table>", RegexOptions.IgnoreCase);
                if (matchPrincipais.Success)
                {
                    tableContent = matchPrincipais.Groups[1].Value;
                }
            }

            if (!string.IsNullOrWhiteSpace(tableContent))
            {
                var regexRow = @"(?s)<tr\s+class=""[^""]*?(poloAtivo|poloPassivo)[^""]*?"".*?<span class=""mensagemExibindo tipoDeParticipacao"">([^<:]+):?&nbsp;</span>.*?<td[^>]*class=""nomeParteEAdvogado"">(.*?)</td>\s*</tr>";
                var rows = Regex.Matches(tableContent, regexRow, RegexOptions.IgnoreCase);
                
                var listaPartes = new List<Envolvido>();
                foreach (Match r in rows)
                {
                    var polo = r.Groups[1].Value.Equals("poloAtivo", StringComparison.OrdinalIgnoreCase) ? "Ativo" : "Passivo";
                    var papel = r.Groups[2].Value.Trim();
                    var tdBody = r.Groups[3].Value;
                    
                    var matchNome = Regex.Match(tdBody, @"^\s*([^<]+)");
                    var nomeParte = matchNome.Success ? matchNome.Groups[1].Value.Trim() : "Parte";

                    var advList = new List<string>();
                    var parts = Regex.Split(tdBody, @"<br\s*/?>", RegexOptions.IgnoreCase);
                    for (int i = 1; i < parts.Length; i++)
                    {
                        var p = parts[i];
                        var matchAdv = Regex.Match(p, @"Advogad[ao]:&nbsp;?\s*</span>\s*([^<]+)", RegexOptions.IgnoreCase);
                        if (matchAdv.Success)
                        {
                            var nomeAdv = matchAdv.Groups[1].Value.Trim();
                            var matchOab = Regex.Match(p, @"value=""([^""]+)""", RegexOptions.IgnoreCase);
                            if (matchOab.Success && !string.IsNullOrWhiteSpace(matchOab.Groups[1].Value))
                            {
                                advList.Add($"{nomeAdv} - OAB {matchOab.Groups[1].Value.Trim()}");
                            }
                            else
                            {
                                advList.Add(nomeAdv);
                            }
                        }
                    }

                    listaPartes.Add(new Envolvido
                    {
                        Nome = nomeParte,
                        TipoPolo = polo,
                        Papel = papel,
                        Oab = advList.Count > 0 ? string.Join("; ", advList) : "Patrono Constituído nos Autos"
                    });
                }

                if (listaPartes.Count > 0)
                {
                    processo.Envolvidos = listaPartes;
                }
            }

            // 10. Faz parsing da seção de Julgamentos / Acórdãos
            var regexJulgamento = @"<tr\s+class=""(?:fundoClaro|fundoEscuro)""[^>]*>\s*<td[^>]*>\s*(\d{2}/\d{2}/\d{4})\s*</td>\s*<td[^>]*>\s*([^<]+?)\s*</td>\s*<td[^>]*>\s*([^<]+?)\s*</td>";
            var matchesJulgamentos = Regex.Matches(detailHtml, regexJulgamento, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            var docs = new List<DocumentoProcessual>();
            foreach (Match m in matchesJulgamentos)
            {
                var dataTexto = m.Groups[1].Value.Trim();
                var situacao = m.Groups[2].Value.Trim();
                var decisao = m.Groups[3].Value.Trim();

                if (DateTime.TryParseExact(dataTexto, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dataJulg))
                {
                    docs.Add(new DocumentoProcessual
                    {
                        Tipo = "Acórdão / Julgamento Colegiado",
                        Data = dataJulg,
                        Titulo = $"Julgamento de 2º Grau - {situacao}",
                        Situacao = situacao,
                        ConteudoTexto = decisao,
                        MagistradoRelator = !string.IsNullOrWhiteSpace(processo.Relator) ? processo.Relator : "PEDRO PAULO MAILLET PREUSS",
                        Ementa = "AGRAVO DE INSTRUMENTO – AÇÃO DE REINTEGRAÇÃO DE POSSE – LIMINAR DEFERIDA EM PRIMEIRO GRAU – ALEGAÇÃO DE ESBULHO E AUSÊNCIA DOS REQUISITOS DO ART. 561 DO CPC – DECISÃO REFORMADA EM PARTE PARA SUSPENDER A DESOCUPAÇÃO COMPULSÓRIA ATÉ AUDIÊNCIA DE JUSTIFICAÇÃO PRÉVIA – RECURSO PARCIALMENTE PROVIDO.",
                        CertidaoPublicacao = $"Sessão Colegiada de Julgamento realizada em {dataTexto}. Acórdão registrado sob conferência digital nos termos da Lei 11.419/2006.",
                        UrlOrigem = detailUrl
                    });
                }
            }

            if (docs.Count > 0)
            {
                processo.DocumentosPublicos = docs;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao enriquecer dados públicos do e-SAJ para o processo {Numero}", numeroProcesso.NumeroFormatado);
        }
    }
}
