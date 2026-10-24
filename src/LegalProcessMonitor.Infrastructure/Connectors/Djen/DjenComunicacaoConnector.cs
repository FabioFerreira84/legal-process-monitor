using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;
using LegalProcessMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace LegalProcessMonitor.Infrastructure.Connectors.Djen;

public class DjenComunicacaoConnector : IDjenComunicacaoConnector
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DjenComunicacaoConnector> _logger;
    private const string BaseUrl = "https://comunicaapi.pje.jus.br/api/v1/comunicacao";

    public DjenComunicacaoConnector(HttpClient httpClient, ILogger<DjenComunicacaoConnector> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task EnriquecerProcessoAsync(Processo processo, NumeroProcessoCnj numeroProcesso, CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl}?numeroProcesso={Uri.EscapeDataString(numeroProcesso.ApenasDigitos)}&itensPorPagina=30&pagina=1";
            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("API DJEN respondeu com {StatusCode} para o processo {Numero}", response.StatusCode, numeroProcesso.NumeroFormatado);
                return;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            AplicarJsonAoProcesso(json, processo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao consultar publicações oficiais no DJEN para o processo {Numero}", numeroProcesso.NumeroFormatado);
        }
    }

    public async Task EnriquecerCarteiraPorOabAsync(AdvogadoCarteira carteira, string numeroInscricaoOab, string ufOab, CancellationToken ct = default)
    {
        try
        {
            var numLimpo = Regex.Replace(numeroInscricaoOab ?? "", @"\D", "");
            var ufLimpa = Regex.Replace(ufOab ?? "SP", @"[^a-zA-Z]", "").ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(ufLimpa)) ufLimpa = "SP";

            if (string.IsNullOrWhiteSpace(numLimpo))
                return;

            var url = $"{BaseUrl}?numeroOab={Uri.EscapeDataString(numLimpo)}&ufOab={Uri.EscapeDataString(ufLimpa)}&itensPorPagina=100&pagina=1";
            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                carteira.AvisosOuErros.Add($"DJEN/CNJ retornou HTTP {(int)response.StatusCode}.");
                return;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            AplicarJsonACarteiraOab(json, carteira, numLimpo, ufLimpa);
            if (!carteira.FontesConsultadas.Contains("DJEN/CNJ Oficial (comunicaapi.pje.jus.br)"))
            {
                carteira.FontesConsultadas.Add("DJEN/CNJ Oficial (comunicaapi.pje.jus.br)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao consultar DJEN por OAB {Oab}/{Uf}", numeroInscricaoOab, ufOab);
            carteira.AvisosOuErros.Add($"Indisponibilidade temporária na consulta DJEN/CNJ: {ex.Message}");
        }
    }

    public static void AplicarJsonAoProcesso(string json, Processo processo)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in items.EnumerateArray())
        {
            var idCom = item.TryGetProperty("id", out var idProp) ? idProp.ToString() : Guid.NewGuid().ToString();
            var dataDispStr = item.TryGetProperty("data_disponibilizacao", out var dtProp) ? dtProp.GetString() : null;
            var siglaTribunal = item.TryGetProperty("siglaTribunal", out var tribProp) ? tribProp.GetString() ?? processo.Tribunal : processo.Tribunal;
            var tipoDoc = item.TryGetProperty("tipoDocumento", out var tdProp) ? tdProp.GetString() ?? "Publicação DJEN" : "Publicação DJEN";
            var nomeOrgao = item.TryGetProperty("nomeOrgao", out var orgProp) ? orgProp.GetString() ?? "" : "";
            var nomeClasse = item.TryGetProperty("nomeClasse", out var clProp) ? clProp.GetString() ?? "" : "";
            var link = item.TryGetProperty("link", out var lkProp) ? lkProp.GetString() ?? "" : "";
            var textoBruto = item.TryGetProperty("texto", out var txProp) ? txProp.GetString() ?? "" : "";

            DateTime dataDisp = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(dataDispStr) &&
                DateTime.TryParseExact(dataDispStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDt))
            {
                // Mantém meio-dia UTC para evitar deslocamento de dia em fuso horário America/Sao_Paulo (-03:00)
                dataDisp = new DateTime(parsedDt.Year, parsedDt.Month, parsedDt.Day, 12, 0, 0, DateTimeKind.Utc);
            }

            var textoLimpo = LimparHtml(textoBruto);
            var ementaExtraida = ExtrairEmentaOuResumo(textoLimpo, tipoDoc, nomeClasse);
            var relatorExtraido = ExtrairRelatorDoTexto(textoLimpo);

            if (!string.IsNullOrWhiteSpace(relatorExtraido) && string.IsNullOrWhiteSpace(processo.Relator))
            {
                processo.Relator = relatorExtraido;
            }

            var docId = $"djen-{idCom}";
            if (!processo.DocumentosPublicos.Exists(d => d.Id == docId))
            {
                processo.DocumentosPublicos.Add(new DocumentoProcessual
                {
                    Id = docId,
                    Tipo = $"DJEN ({siglaTribunal}) - {tipoDoc}",
                    Data = dataDisp,
                    Titulo = !string.IsNullOrWhiteSpace(nomeClasse) ? $"{tipoDoc} — {nomeClasse} ({siglaTribunal})" : $"{tipoDoc} ({siglaTribunal})",
                    Situacao = "Publicado no DJEN/CNJ",
                    MagistradoRelator = !string.IsNullOrWhiteSpace(relatorExtraido) ? relatorExtraido : processo.Relator,
                    Ementa = ementaExtraida,
                    ConteudoTexto = textoLimpo,
                    CertidaoPublicacao = $"Disponibilizado oficialmente no Diário de Justiça Eletrônico Nacional (DJEN/CNJ - ID {idCom}) em {dataDisp:dd/MM/yyyy} pelo órgão '{nomeOrgao}' ({siglaTribunal}).",
                    UrlOrigem = !string.IsNullOrWhiteSpace(link) ? link : "https://comunica.pje.jus.br"
                });
            }

            // Adiciona também na linha do tempo de movimentações se não houver naquela data/órgão
            var tituloMov = $"Disponibilização DJEN ({siglaTribunal}) - {tipoDoc}";
            if (!processo.Movimentacoes.Exists(m => m.DataHora.Date == dataDisp.Date && m.Titulo.Contains(tipoDoc, StringComparison.OrdinalIgnoreCase)))
            {
                processo.AdicionarMovimentacao(new Movimentacao
                {
                    DataHora = dataDisp,
                    Titulo = tituloMov,
                    Descricao = $"{nomeClasse} - {nomeOrgao}. {ementaExtraida}".Trim(),
                    CodigoNacional = 92
                });
            }

            // Se o processo ainda não possui partes ou tem apenas placeholder LGPD, extrai destinatários e advogados reais do DJEN
            if (processo.Envolvidos.Count == 0 || processo.Envolvidos.All(e => e.Nome.Contains("Sigilo", StringComparison.OrdinalIgnoreCase)))
            {
                var advogadosFormatados = new List<string>();
                if (item.TryGetProperty("destinatarioadvogados", out var advsArray) && advsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var advItem in advsArray.EnumerateArray())
                    {
                        if (advItem.TryGetProperty("advogado", out var advObj) && advObj.ValueKind == JsonValueKind.Object)
                        {
                            var nomeAdv = advObj.TryGetProperty("nome", out var na) ? na.GetString() : null;
                            var numOab = advObj.TryGetProperty("numero_oab", out var no) ? no.GetString() : null;
                            var uf = advObj.TryGetProperty("uf_oab", out var uo) ? uo.GetString() : null;
                            if (!string.IsNullOrWhiteSpace(nomeAdv))
                            {
                                advogadosFormatados.Add(!string.IsNullOrWhiteSpace(numOab) ? $"{nomeAdv} - OAB {numOab}{uf}" : nomeAdv);
                            }
                        }
                    }
                }

                if (item.TryGetProperty("destinatarios", out var destArray) && destArray.ValueKind == JsonValueKind.Array)
                {
                    var novosEnvolvidos = new List<Envolvido>();
                    foreach (var dest in destArray.EnumerateArray())
                    {
                        var nomeParte = dest.TryGetProperty("nome", out var np) ? np.GetString() : null;
                        var poloSigla = dest.TryGetProperty("polo", out var pl) ? pl.GetString() : "A";
                        if (string.IsNullOrWhiteSpace(nomeParte)) continue;

                        var nomeLimpo = Regex.Replace(nomeParte, @"^\d+\.\s*", "").Trim();
                        var polo = string.Equals(poloSigla, "P", StringComparison.OrdinalIgnoreCase) ? "Passivo" : "Ativo";
                        novosEnvolvidos.Add(new Envolvido
                        {
                            Nome = nomeLimpo,
                            TipoPolo = polo,
                            Papel = polo == "Ativo" ? "Polo Ativo" : "Polo Passivo",
                            Oab = advogadosFormatados.Count > 0 ? string.Join("; ", advogadosFormatados.Distinct()) : null
                        });
                    }

                    if (novosEnvolvidos.Count > 0)
                    {
                        processo.Envolvidos = novosEnvolvidos;
                    }
                }
            }
        }

        // Ordena documentos públicos do mais recente para o mais antigo
        processo.DocumentosPublicos = processo.DocumentosPublicos
            .OrderByDescending(d => d.Data)
            .ToList();
    }

    public static void AplicarJsonACarteiraOab(string json, AdvogadoCarteira carteira, string numeroInscricaoOab, string ufOab)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("count", out var countProp) && countProp.TryGetInt32(out var totalCount))
        {
            carteira.TotalComunicacoesDjen = totalCount;
        }

        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in items.EnumerateArray())
        {
            var cnjLimpo = item.TryGetProperty("numero_processo", out var npProp) ? Regex.Replace(npProp.GetString() ?? "", @"\D", "") : "";
            var cnjMascara = item.TryGetProperty("numeroprocessocommascara", out var nmProp) ? nmProp.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(cnjLimpo) || cnjLimpo.Length != 20)
                continue;

            if (string.IsNullOrWhiteSpace(cnjMascara) && NumeroProcessoCnj.TryParse(cnjLimpo, out var parsedCnj) && parsedCnj != null)
            {
                cnjMascara = parsedCnj.NumeroFormatado;
            }

            // Identifica o nome oficial do advogado pesquisado nos registros do DJEN
            if (string.IsNullOrWhiteSpace(carteira.NomeAdvogado) &&
                item.TryGetProperty("destinatarioadvogados", out var advsArray) &&
                advsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var advItem in advsArray.EnumerateArray())
                {
                    if (advItem.TryGetProperty("advogado", out var advObj) && advObj.ValueKind == JsonValueKind.Object)
                    {
                        var num = advObj.TryGetProperty("numero_oab", out var no) ? Regex.Replace(no.GetString() ?? "", @"\D", "") : "";
                        var nomeAdv = advObj.TryGetProperty("nome", out var na) ? na.GetString() : null;
                        if (num == numeroInscricaoOab && !string.IsNullOrWhiteSpace(nomeAdv))
                        {
                            carteira.NomeAdvogado = nomeAdv.Trim();
                            break;
                        }
                    }
                }
            }

            var existente = carteira.Processos.FirstOrDefault(p => p.NumeroCnjLimpo == cnjLimpo);
            if (existente != null)
                continue;

            var siglaTribunal = item.TryGetProperty("siglaTribunal", out var tribProp) ? tribProp.GetString() ?? "TJSP" : "TJSP";
            var nomeClasse = item.TryGetProperty("nomeClasse", out var clProp) ? clProp.GetString() ?? "Processo Judicial" : "Processo Judicial";
            var tipoDoc = item.TryGetProperty("tipoDocumento", out var tdProp) ? tdProp.GetString() ?? "" : "";
            var nomeOrgao = item.TryGetProperty("nomeOrgao", out var orgProp) ? orgProp.GetString() ?? "" : "";
            var dataDisp = item.TryGetProperty("datadisponibilizacao", out var ddProp) ? ddProp.GetString() ?? "" : "";
            var link = item.TryGetProperty("link", out var lkProp) ? lkProp.GetString() ?? "" : "";
            var textoBruto = item.TryGetProperty("texto", out var txProp) ? txProp.GetString() ?? "" : "";
            var textoLimpo = LimparHtml(textoBruto);

            var grau = siglaTribunal is "STJ" or "STF" or "TST" ? 3 :
                       (cnjLimpo.EndsWith("0000") || nomeOrgao.Contains("Câmara", StringComparison.OrdinalIgnoreCase) || nomeOrgao.Contains("Turma", StringComparison.OrdinalIgnoreCase) ? 2 : 1);

            carteira.Processos.Add(new ProcessoResumo
            {
                NumeroCnjFormatado = !string.IsNullOrWhiteSpace(cnjMascara) ? cnjMascara : cnjLimpo,
                NumeroCnjLimpo = cnjLimpo,
                Tribunal = siglaTribunal,
                Grau = grau,
                Classe = !string.IsNullOrWhiteSpace(tipoDoc) ? $"{nomeClasse} ({tipoDoc})" : nomeClasse,
                Vara = nomeOrgao,
                Papel = $"Intimado via DJEN (OAB {numeroInscricaoOab}/{ufOab})",
                DataRecebimento = dataDisp,
                UrlOrigem = !string.IsNullOrWhiteSpace(link) ? link : "https://comunica.pje.jus.br",
                FonteDados = "DJEN/CNJ Oficial",
                UltimaComunicacaoResumo = textoLimpo.Length > 220 ? textoLimpo[..220] + "..." : textoLimpo
            });
        }
    }

    private static string LimparHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var semTags = Regex.Replace(html, @"<tr[^>]*>", "\n", RegexOptions.IgnoreCase);
        semTags = Regex.Replace(semTags, @"</p>|</div>|<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        semTags = Regex.Replace(semTags, @"<[^>]+>", " ");
        semTags = WebUtility.HtmlDecode(semTags);
        semTags = Regex.Replace(semTags, @"[ \t]+", " ");
        semTags = Regex.Replace(semTags, @"\n\s*\n+", "\n");
        return semTags.Trim();
    }

    private static string ExtrairEmentaOuResumo(string textoLimpo, string tipoDoc, string nomeClasse)
    {
        if (string.IsNullOrWhiteSpace(textoLimpo))
            return $"{tipoDoc} - {nomeClasse}";

        // Procura bloco em caixa alta típico de ementa (ex: "AGRAVO DE INSTRUMENTO. AÇÃO DE ..." ou "EMBARGOS DE DECLARAÇÃO.")
        var matchEmenta = Regex.Match(textoLimpo, @"((?:AGRAVO DE INSTRUMENTO|EMBARGOS DE DECLARAÇÃO|APELAÇÃO|MANDADO DE SEGURANÇA|DECISÃO)\s*[\.\-]\s*[^\n]{40,900})", RegexOptions.IgnoreCase);
        if (matchEmenta.Success)
        {
            var trecho = matchEmenta.Groups[1].Value.Trim();
            // Corta antes de avisos padronizados de custas de preparo recursal ("ART. 1007 CPC - EVENTUAL RECURSO")
            var idxCustas = trecho.IndexOf("ART. 1007 CPC", StringComparison.OrdinalIgnoreCase);
            if (idxCustas > 30)
                trecho = trecho[..idxCustas].Trim();
            return trecho;
        }

        return textoLimpo.Length > 360 ? textoLimpo[..360].Trim() + "..." : textoLimpo;
    }

    private static string? ExtrairRelatorDoTexto(string textoLimpo)
    {
        var m1 = Regex.Match(textoLimpo, @"RELATOR\s*:\s*([^\n\r]+)", RegexOptions.IgnoreCase);
        if (m1.Success) return m1.Groups[1].Value.Trim();

        var m2 = Regex.Match(textoLimpo, @"Magistrado\(a\)\s+([A-ZÀ-Úa-zà-ú\s]{5,60}?)\s+-", RegexOptions.None);
        if (m2.Success) return m2.Groups[1].Value.Trim();

        return null;
    }
}
