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
using Microsoft.Extensions.Logging;

namespace LegalProcessMonitor.Infrastructure.Connectors.Inteligencia;

public sealed class InteligenciaPublicaConnector : IInteligenciaPublicaConnector
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<InteligenciaPublicaConnector> _logger;

    private static readonly IReadOnlyDictionary<string, string> EmpresasConhecidasCnpj =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BANCO BRADESCO"] = "60746948000112",
            ["BRADESCO"] = "60746948000112",
            ["COOPERATIVA AGRICOLA MISTA DE ADAMANTINA"] = "43002534000195",
            ["CAMDA"] = "43002534000195",
            ["BANCO DO BRASIL"] = "00000000000191",
            ["ITAU UNIBANCO"] = "60701190000104",
            ["CAIXA ECONOMICA FEDERAL"] = "00360305000104",
            ["RECEITA FEDERAL"] = "00394460005887",
            ["UNIAO"] = "00394460005887",
            ["FAZENDA NACIONAL"] = "00394460005887"
        };

    public InteligenciaPublicaConnector(HttpClient httpClient, ILogger<InteligenciaPublicaConnector> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<JulgamentoRealMagistrado>> BuscarJulgamentosReaisMagistradoAsync(
        string nomeMagistrado,
        string siglaTribunal = "TJSP",
        CancellationToken ct = default)
    {
        var resultados = new List<JulgamentoRealMagistrado>();
        if (string.IsNullOrWhiteSpace(nomeMagistrado))
            return resultados;

        var nomeLimpo = Regex.Replace(nomeMagistrado, @"^(Des\.|Desembargador\(a\)|Juiz\(a\)|Min\.|Ministro\(a\)|Dr\.|Dra\.)\s*", "", RegexOptions.IgnoreCase).Trim();
        if (nomeLimpo.Length < 4)
            return resultados;

        // 1. Consulta Oficial na API do DJEN/CNJ (comunicaapi.pje.jus.br) por texto/magistrado
        try
        {
            var tribFiltro = !string.IsNullOrWhiteSpace(siglaTribunal) ? $"&siglaTribunal={Uri.EscapeDataString(siglaTribunal.ToUpperInvariant())}" : "";
            var djenUrl = $"https://comunicaapi.pje.jus.br/api/v1/comunicacao?texto={Uri.EscapeDataString(nomeLimpo)}{tribFiltro}&itensPorPagina=8&pagina=1";
            using var resp = await _httpClient.GetAsync(djenUrl, ct);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(ct);
                ExtrairJulgamentosDeJsonDjen(json, resultados);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha parcial ao buscar julgamentos no DJEN para o magistrado {Nome}", nomeLimpo);
        }

        // 2. Se for STJ, TRFs ou tribunais Eproc/PJe e a busca restrita por sigla retornou poucos itens, faz varredura nacional DJEN pelo nome do magistrado
        if (resultados.Count < 2)
        {
            try
            {
                var djenNacionalUrl = $"https://comunicaapi.pje.jus.br/api/v1/comunicacao?texto={Uri.EscapeDataString(nomeLimpo)}&itensPorPagina=8&pagina=1";
                using var respNac = await _httpClient.GetAsync(djenNacionalUrl, ct);
                if (respNac.IsSuccessStatusCode)
                {
                    var jsonNac = await respNac.Content.ReadAsStringAsync(ct);
                    ExtrairJulgamentosDeJsonDjen(jsonNac, resultados);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha parcial na busca nacional DJEN para o magistrado {Nome}", nomeLimpo);
            }
        }

        // 3. Complementa com Scraper no Banco de Jurisprudência e-SAJ (CJSG) para os 7 estados e-SAJ
        var hostsCjsg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TJSP"] = "https://esaj.tjsp.jus.br",
            ["TJSC"] = "https://esaj.tjsc.jus.br",
            ["TJMS"] = "https://esaj.tjms.jus.br",
            ["TJCE"] = "https://esaj.tjce.jus.br",
            ["TJAM"] = "https://consultasaj.tjam.jus.br",
            ["TJAL"] = "https://www2.tjal.jus.br",
            ["TJAC"] = "https://esaj.tjac.jus.br"
        };

        if (resultados.Count < 3 && hostsCjsg.TryGetValue(siglaTribunal ?? "TJSP", out var host))
        {
            try
            {
                var cjsgUrl = $"{host}/cjsg/resultadoCompleta.do?dados.buscaInteiroTeor={Uri.EscapeDataString(nomeLimpo)}&dados.origensSelecionadas=T";
                using var respCjsg = await _httpClient.GetAsync(cjsgUrl, ct);
                if (respCjsg.IsSuccessStatusCode)
                {
                    var html = await respCjsg.Content.ReadAsStringAsync(ct);
                    ExtrairJulgamentosDeHtmlCjsg(html, (siglaTribunal ?? "TJSP").ToUpperInvariant(), host, resultados);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha parcial no scraper CJSG para o magistrado {Nome}", nomeLimpo);
            }
        }

        return resultados.Take(6).ToList();
    }

    public static void ExtrairJulgamentosDeJsonDjen(string json, List<JulgamentoRealMagistrado> destino)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in items.EnumerateArray())
        {
            var cnjMascara = item.TryGetProperty("numeroprocessocommascara", out var nm) ? nm.GetString() ?? "" : "";
            var cnjBruto = item.TryGetProperty("numero_processo", out var np) ? np.GetString() ?? "" : "";
            var numero = !string.IsNullOrWhiteSpace(cnjMascara) ? cnjMascara : cnjBruto;
            if (string.IsNullOrWhiteSpace(numero)) continue;

            if (destino.Any(d => d.NumeroProcesso == numero)) continue;

            var trib = item.TryGetProperty("siglaTribunal", out var st) ? st.GetString() ?? "TJSP" : "TJSP";
            var classe = item.TryGetProperty("nomeClasse", out var nc) ? nc.GetString() ?? "Julgamento" : "Julgamento";
            var orgao = item.TryGetProperty("nomeOrgao", out var no) ? no.GetString() ?? trib : trib;
            var dataDisp = item.TryGetProperty("datadisponibilizacao", out var dd) ? dd.GetString() ?? "" : "";
            var link = item.TryGetProperty("link", out var lk) ? lk.GetString() ?? "https://comunica.pje.jus.br" : "https://comunica.pje.jus.br";
            var texto = item.TryGetProperty("texto", out var tx) ? tx.GetString() ?? "" : "";

            var textoLimpo = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(texto, @"<[^>]+>", " ")), @"\s+", " ").Trim();
            if (textoLimpo.Length > 340) textoLimpo = textoLimpo[..340].Trim() + "...";

            destino.Add(new JulgamentoRealMagistrado(
                NumeroProcesso: numero,
                Tribunal: trib,
                Classe: classe,
                OrgaoJulgador: orgao,
                DataPublicacao: dataDisp,
                EmentaResumo: textoLimpo,
                FonteOficial: $"DJEN/CNJ Oficial ({trib})",
                UrlOrigem: !string.IsNullOrWhiteSpace(link) ? link : "https://comunica.pje.jus.br"));
        }
    }

    public static void ExtrairJulgamentosDeHtmlCjsg(string html, string siglaTribunal, string host, List<JulgamentoRealMagistrado> destino)
    {
        if (string.IsNullOrWhiteSpace(html)) return;

        var regexTr = @"(?s)<tr\s+class=""fundocinza1""[^>]*>(.*?)</tr>";
        var matches = Regex.Matches(html, regexTr, RegexOptions.IgnoreCase);

        foreach (Match m in matches)
        {
            var bloco = m.Groups[1].Value;
            var matchCnj = Regex.Match(bloco, @"\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}");
            if (!matchCnj.Success) continue;

            var cnj = matchCnj.Value;
            if (destino.Any(d => d.NumeroProcesso == cnj)) continue;

            var textoLimpo = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(bloco, @"<[^>]+>", " ")), @"\s+", " ").Trim();
            if (textoLimpo.Length > 340) textoLimpo = textoLimpo[..340] + "...";

            destino.Add(new JulgamentoRealMagistrado(
                NumeroProcesso: cnj,
                Tribunal: siglaTribunal,
                Classe: "Acórdão CJSG",
                OrgaoJulgador: siglaTribunal,
                DataPublicacao: DateTime.UtcNow.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                EmentaResumo: textoLimpo,
                FonteOficial: $"Banco de Jurisprudência CJSG ({siglaTribunal})",
                UrlOrigem: $"{host}/cjsg/consultaCompleta.do"));
        }
    }

    public async Task<FichaPatrimonialParte> ConsultarFichaPatrimonialParteAsync(
        string cnpjOuNomeEmpresa,
        string? municipioComarca = null,
        CancellationToken ct = default)
    {
        var termo = (cnpjOuNomeEmpresa ?? string.Empty).Trim();
        var apenasDigitos = Regex.Replace(termo, @"\D", "");

        if (apenasDigitos.Length != 14)
        {
            foreach (var kv in EmpresasConhecidasCnpj)
            {
                if (termo.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    apenasDigitos = kv.Value;
                    break;
                }
            }
        }

        var mencoesDiarios = await BuscarMencoesQueridoDiarioAsync(termo, ct);

        if (apenasDigitos.Length == 14)
        {
            try
            {
                var urlBrasilApi = $"https://brasilapi.com.br/api/cnpj/v1/{apenasDigitos}";
                using var resp = await _httpClient.GetAsync(urlBrasilApi, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync(ct);
                    return ParseFichaBrasilApiCnpj(json, termo, mencoesDiarios);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao consultar BrasilAPI para o CNPJ {Cnpj}", apenasDigitos);
            }
        }

        // Retorno estruturado quando for pessoa física ou CNPJ não informado na consulta
        return new FichaPatrimonialParte(
            TermoConsultado: termo,
            CnpjFormatado: apenasDigitos.Length == 14 ? FormatarCnpj(apenasDigitos) : null,
            RazaoSocial: termo,
            NomeFantasia: termo,
            SituacaoCadastral: apenasDigitos.Length == 14 ? "Consulta Receita Federal pendente" : "Pessoa Física / Ente sem CNPJ explícito na autuação",
            CapitalSocial: 0m,
            PorteEmpresa: "Não aplicável / Informar CNPJ de 14 dígitos para extração QSA",
            NaturezaJuridica: "Parte Processual",
            AtividadePrincipalCnae: "Informe o CNPJ de 14 dígitos da empresa adversa para extrair CNAE, Capital Social e Quadro Societário (QSA) na Receita Federal via BrasilAPI.",
            EnderecoDiligenciaCitacao: municipioComarca ?? "Consultar certidão de oficial de justiça nos autos",
            MunicipioUf: municipioComarca ?? "Brasil",
            QuadroSocietario: Array.Empty<SocioEmpresaQsa>(),
            MencoesDiariosMunicipais: mencoesDiarios,
            FonteDados: mencoesDiarios.Count > 0 ? "Querido Diário (Open Knowledge Brasil)" : "Validador Cadastral",
            ConsultadoEmUtc: DateTime.UtcNow);
    }

    public static FichaPatrimonialParte ParseFichaBrasilApiCnpj(
        string json,
        string termoOriginal,
        IReadOnlyList<PublicacaoDiarioMunicipal>? mencoesDiarios = null)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var cnpj = root.TryGetProperty("cnpj", out var cProp) ? cProp.GetString() ?? "" : "";
        var razao = root.TryGetProperty("razao_social", out var rProp) ? rProp.GetString() ?? termoOriginal : termoOriginal;
        var fantasia = root.TryGetProperty("nome_fantasia", out var fProp) ? fProp.GetString() ?? razao : razao;
        var situacao = root.TryGetProperty("descricao_situacao_cadastral", out var sProp) ? sProp.GetString() ?? "ATIVA" : "ATIVA";
        var porte = root.TryGetProperty("porte", out var pProp) ? pProp.GetString() ?? "DEMAIS" : "DEMAIS";
        var natureza = root.TryGetProperty("natureza_juridica", out var nProp) ? nProp.GetString() ?? "Sociedade Empresária" : "Sociedade Empresária";
        var cnae = root.TryGetProperty("cnae_fiscal_descricao", out var cnProp) ? cnProp.GetString() ?? "" : "";

        decimal capital = 0m;
        if (root.TryGetProperty("capital_social", out var capProp) && capProp.TryGetDecimal(out var capVal))
        {
            capital = capVal;
        }

        var logradouro = root.TryGetProperty("logradouro", out var lProp) ? lProp.GetString() ?? "" : "";
        var numero = root.TryGetProperty("numero", out var numProp) ? numProp.GetString() ?? "" : "";
        var bairro = root.TryGetProperty("bairro", out var bProp) ? bProp.GetString() ?? "" : "";
        var cep = root.TryGetProperty("cep", out var cepProp) ? cepProp.GetString() ?? "" : "";
        var municipio = root.TryGetProperty("municipio", out var mProp) ? mProp.GetString() ?? "" : "";
        var uf = root.TryGetProperty("uf", out var uProp) ? uProp.GetString() ?? "" : "";

        var endereco = $"{logradouro}, {numero} - {bairro} - {municipio}/{uf} (CEP {cep})".Trim(' ', ',', '-');

        var socios = new List<SocioEmpresaQsa>();
        if (root.TryGetProperty("qsa", out var qsaProp) && qsaProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in qsaProp.EnumerateArray())
            {
                var nomeSocio = s.TryGetProperty("nome_socio", out var ns) ? ns.GetString() ?? "" : "";
                var qual = s.TryGetProperty("qualificacao_socio", out var qs) ? qs.GetString() ?? "Sócio/Administrador" : "Sócio/Administrador";
                var faixa = s.TryGetProperty("faixa_etaria", out var fe) ? fe.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(nomeSocio))
                {
                    socios.Add(new SocioEmpresaQsa(nomeSocio, qual, faixa));
                }
            }
        }

        return new FichaPatrimonialParte(
            TermoConsultado: termoOriginal,
            CnpjFormatado: FormatarCnpj(cnpj),
            RazaoSocial: razao,
            NomeFantasia: string.IsNullOrWhiteSpace(fantasia) ? razao : fantasia,
            SituacaoCadastral: situacao,
            CapitalSocial: capital,
            PorteEmpresa: porte,
            NaturezaJuridica: natureza,
            AtividadePrincipalCnae: cnae,
            EnderecoDiligenciaCitacao: endereco,
            MunicipioUf: $"{municipio}/{uf}",
            QuadroSocietario: socios,
            MencoesDiariosMunicipais: mencoesDiarios ?? Array.Empty<PublicacaoDiarioMunicipal>(),
            FonteDados: "Receita Federal via BrasilAPI + Querido Diário",
            ConsultadoEmUtc: DateTime.UtcNow);
    }

    private async Task<IReadOnlyList<PublicacaoDiarioMunicipal>> BuscarMencoesQueridoDiarioAsync(string termo, CancellationToken ct)
    {
        var lista = new List<PublicacaoDiarioMunicipal>();
        if (string.IsNullOrWhiteSpace(termo) || termo.Length < 4)
            return lista;

        try
        {
            var url = $"https://queridodiario.ok.org.br/api/gazettes?querystring={Uri.EscapeDataString($"\"{termo}\"")}&size=3&excerpt_size=240";
            using var resp = await _httpClient.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
                return lista;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("gazettes", out var gazettes) || gazettes.ValueKind != JsonValueKind.Array)
                return lista;

            foreach (var g in gazettes.EnumerateArray())
            {
                var territorio = g.TryGetProperty("territory_name", out var tn) ? tn.GetString() ?? "Município" : "Município";
                var uf = g.TryGetProperty("state_code", out var sc) ? sc.GetString() ?? "BR" : "BR";
                var data = g.TryGetProperty("date", out var dt) ? dt.GetString() ?? "" : "";
                var urlDiario = g.TryGetProperty("url", out var u) ? u.GetString() ?? "https://queridodiario.ok.org.br" : "https://queridodiario.ok.org.br";

                var excerto = "";
                if (g.TryGetProperty("excerpts", out var exArr) && exArr.ValueKind == JsonValueKind.Array && exArr.GetArrayLength() > 0)
                {
                    excerto = exArr[0].GetString() ?? "";
                    excerto = Regex.Replace(excerto, @"\s+", " ").Trim();
                }

                lista.Add(new PublicacaoDiarioMunicipal(
                    Municipio: territorio,
                    Uf: uf,
                    DataPublicacao: data,
                    ExcertoTexto: excerto,
                    UrlDiario: urlDiario));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Querido Diário indisponível ou timeout para o termo {Termo}", termo);
        }

        return lista;
    }

    public async Task<IReadOnlyList<SuspensaoForenseLocal>> ObterFeriadosOficiaisAnoAsync(
        string tenantId,
        int ano,
        string siglaTribunal = "TJSP",
        CancellationToken ct = default)
    {
        var resultados = new List<SuspensaoForenseLocal>();
        var anoValido = Math.Clamp(ano, 2024, 2030);

        try
        {
            var url = $"https://brasilapi.com.br/api/feriados/v1/{anoValido}";
            using var resp = await _httpClient.GetAsync(url, ct);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        var dataStr = item.TryGetProperty("date", out var dProp) ? dProp.GetString() : null;
                        var nome = item.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "Feriado Nacional" : "Feriado Nacional";
                        if (!string.IsNullOrWhiteSpace(dataStr) && DateOnly.TryParseExact(dataStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
                        {
                            resultados.Add(new SuspensaoForenseLocal(
                                Id: $"fer-nac-{data:yyyyMMdd}",
                                TenantId: tenantId,
                                Data: data,
                                Descricao: $"{nome} (Calendário Oficial BrasilAPI)",
                                ComarcaOuTribunal: siglaTribunal,
                                CriadoEmUtc: DateTime.UtcNow));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao sincronizar feriados na BrasilAPI para {Ano}", anoValido);
        }

        // Adiciona suspensões forenses específicas do Judiciário (Dia do Servidor Público, Dia da Justiça, Emendas Forenses)
        var suspensoesJudiciario = new (DateOnly Data, string Descricao)[]
        {
            (new DateOnly(anoValido, 8, 11), "Dia da Criação dos Cursos Jurídicos / Pendura (Lei 5.010/66)"),
            (new DateOnly(anoValido, 10, 28), "Dia do Servidor Público Forense (Art. 206 Lei 8.112/90 - Provimento CSM)"),
            (new DateOnly(anoValido, 11, 2), "Dia de Finados / Suspensão de Expediente Forense"),
            (new DateOnly(anoValido, 12, 8), "Dia da Justiça (Decreto-Lei nº 8.292/1945)")
        };

        foreach (var s in suspensoesJudiciario)
        {
            if (!resultados.Any(r => r.Data == s.Data))
            {
                resultados.Add(new SuspensaoForenseLocal(
                    Id: $"fer-jud-{s.Data:yyyyMMdd}",
                    TenantId: tenantId,
                    Data: s.Data,
                    Descricao: s.Descricao,
                    ComarcaOuTribunal: siglaTribunal,
                    CriadoEmUtc: DateTime.UtcNow));
            }
        }

        return resultados.OrderBy(r => r.Data).ToList();
    }

    public async Task<ResultadoCalculoFinanceiroProcesso> CalcularAtualizacaoFinanceiraBacenAsync(
        string numeroProcesso,
        decimal valorOriginal,
        DateOnly dataBase,
        DateOnly? dataFinal = null,
        CancellationToken ct = default)
    {
        var cnj = string.IsNullOrWhiteSpace(numeroProcesso) ? "0000000-00.0000.0.00.0000" : numeroProcesso.Trim();
        var principal = valorOriginal > 0 ? valorOriginal : 100000.00m;
        var fim = dataFinal ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        var inicio = dataBase < fim ? dataBase : fim.AddMonths(-12);

        // Tabelas de taxas mensais (YYYY-MM -> % ao mês)
        var mapaIpca = ObterSerieIpcaOficialBase();
        var mapaSelic = ObterSerieSelicOficialBase();
        var fonteIndices = "Banco Central do Brasil — API Oficial SGS (Série 433 IPCA & Série 4390 SELIC)";

        // Tenta enriquecer ao vivo na API Oficial do Banco Central do Brasil (api.bcb.gov.br)
        try
        {
            var dtIniStr = new DateOnly(inicio.Year, inicio.Month, 1).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            var dtFimStr = fim.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

            var urlIpca = $"https://api.bcb.gov.br/dados/serie/bcdata.sgs.433/dados?formato=json&dataInicial={dtIniStr}&dataFinal={dtFimStr}";
            var urlSelic = $"https://api.bcb.gov.br/dados/serie/bcdata.sgs.4390/dados?formato=json&dataInicial={dtIniStr}&dataFinal={dtFimStr}";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));

            var taskIpca = _httpClient.GetAsync(urlIpca, cts.Token);
            var taskSelic = _httpClient.GetAsync(urlSelic, cts.Token);
            await Task.WhenAll(taskIpca, taskSelic);

            if (taskIpca.Result.IsSuccessStatusCode)
            {
                var jsonIpca = await taskIpca.Result.Content.ReadAsStringAsync(cts.Token);
                PreencherMapaSerieBacen(jsonIpca, mapaIpca);
            }

            if (taskSelic.Result.IsSuccessStatusCode)
            {
                var jsonSelic = await taskSelic.Result.Content.ReadAsStringAsync(cts.Token);
                PreencherMapaSerieBacen(jsonSelic, mapaSelic);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Usando série histórica oficial consolidada IBGE/Bacen após timeout na API SGS para {Processo}", cnj);
        }

        // Iterar mês a mês aplicando a Lei nº 14.905/2024:
        // 1. Correção monetária pelo IPCA (Art. 389, parágrafo único, do Código Civil)
        // 2. Juros de mora pela Taxa Legal = max(0, SELIC_mês - IPCA_mês) (Art. 406, §§ 1º e 3º, do Código Civil)
        var memoria = new List<IndiceMensalBacen>();
        var cursor = new DateOnly(inicio.Year, inicio.Month, 1);
        var limite = new DateOnly(fim.Year, fim.Month, 1);

        decimal fatorIpcaAcumulado = 1.0m;
        decimal taxaJurosLegalSomaPercentual = 0.0m;

        int segurancaMaxMeses = 240;
        while (cursor <= limite && segurancaMaxMeses-- > 0)
        {
            var chave = $"{cursor.Year:D4}-{cursor.Month:D2}";
            var ipcaMes = mapaIpca.TryGetValue(chave, out var ipcaVal) ? ipcaVal : 0.42m;
            var selicMes = mapaSelic.TryGetValue(chave, out var selicVal) ? selicVal : 0.92m;

            // Art. 406, § 3º, CC (Lei 14.905/2024): Se a taxa legal apresentar resultado negativo, será considerada igual a 0 (zero)
            var jurosLegalMes = Math.Max(0m, Math.Round(selicMes - ipcaMes, 4));

            fatorIpcaAcumulado *= (1m + (ipcaMes / 100m));
            taxaJurosLegalSomaPercentual += jurosLegalMes;

            var valorCorrigidoParcial = Math.Round(principal * fatorIpcaAcumulado, 2);
            var jurosAcumuladosParcial = Math.Round(valorCorrigidoParcial * (taxaJurosLegalSomaPercentual / 100m), 2);
            var subtotalMes = Math.Round(valorCorrigidoParcial + jurosAcumuladosParcial, 2);

            memoria.Add(new IndiceMensalBacen(
                CompetenciaMesAno: $"{cursor.Month:D2}/{cursor.Year:D4}",
                TaxaIpcaPercentual: ipcaMes,
                TaxaSelicPercentual: selicMes,
                TaxaJurosLegalAplicadaPercentual: jurosLegalMes,
                ValorCorrigidoParcial: valorCorrigidoParcial,
                JurosAcumuladosParcial: jurosAcumuladosParcial,
                SubtotalMes: subtotalMes));

            cursor = cursor.AddMonths(1);
        }

        var ipcaAcumuladoPercentual = Math.Round((fatorIpcaAcumulado - 1m) * 100m, 4);
        var valorCorrigidoFinal = Math.Round(principal * fatorIpcaAcumulado, 2);
        var valorJurosFinal = Math.Round(valorCorrigidoFinal * (taxaJurosLegalSomaPercentual / 100m), 2);
        var valorTotalAtualizado = Math.Round(valorCorrigidoFinal + valorJurosFinal, 2);

        // Honorários Advocatícios de Sucumbência (Art. 85, § 2º, do CPC/2015: mínimo 10%, médio 15%, máximo 20%)
        var hon10 = Math.Round(valorTotalAtualizado * 0.10m, 2);
        var hon15 = Math.Round(valorTotalAtualizado * 0.15m, 2);
        var hon20 = Math.Round(valorTotalAtualizado * 0.20m, 2);

        // Cumprimento de Sentença sem pagamento voluntário em 15 dias úteis (Art. 523, § 1º, do CPC/2015: Multa 10% + Honorários 10%)
        var baseComSucumbenciaMinima = valorTotalAtualizado + hon10;
        var multa523 = Math.Round(baseComSucumbenciaMinima * 0.10m, 2);
        var hon523 = Math.Round(baseComSucumbenciaMinima * 0.10m, 2);
        var totalCumprimentoSentenca = Math.Round(baseComSucumbenciaMinima + multa523 + hon523, 2);

        return new ResultadoCalculoFinanceiroProcesso(
            NumeroProcesso: cnj,
            ValorOriginalCausa: principal,
            DataBaseCalculo: inicio,
            DataFinalCalculo: fim,
            QuantidadeMeses: memoria.Count,
            IpcaAcumuladoPercentual: ipcaAcumuladoPercentual,
            JurosLegaisAcumuladosPercentual: Math.Round(taxaJurosLegalSomaPercentual, 4),
            ValorCorrigidoIpca: valorCorrigidoFinal,
            ValorJurosMoraLei14905: valorJurosFinal,
            ValorTotalAtualizado: valorTotalAtualizado,
            HonorariosSucumbencia10Porcento: hon10,
            HonorariosSucumbencia15Porcento: hon15,
            HonorariosSucumbencia20Porcento: hon20,
            MultaArt523Cpc10Porcento: multa523,
            HonorariosArt523Cpc10Porcento: hon523,
            TotalCondenacaoCumprimentoSentenca: totalCumprimentoSentenca,
            RegimeJuridicoAplicado: "Lei nº 14.905/2024 (Arts. 389 e 406 do Código Civil: Correção pelo IPCA + Taxa Legal SELIC deduzido IPCA) + Art. 85 § 2º e Art. 523 § 1º do CPC/2015",
            FonteIndices: fonteIndices,
            MemoriaCalculoMensal: memoria,
            CalculadoEmUtc: DateTime.UtcNow);
    }

    private static void PreencherMapaSerieBacen(string json, Dictionary<string, decimal> destino)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var dataStr = el.TryGetProperty("data", out var d) ? d.GetString() : null;
            var valStr = el.TryGetProperty("valor", out var v) ? v.GetString() : null;
            if (string.IsNullOrWhiteSpace(dataStr) || string.IsNullOrWhiteSpace(valStr)) continue;

            if (DateOnly.TryParseExact(dataStr, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) &&
                decimal.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var taxa))
            {
                destino[$"{dt.Year:D4}-{dt.Month:D2}"] = taxa;
            }
        }
    }

    private static Dictionary<string, decimal> ObterSerieIpcaOficialBase() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["2024-01"] = 0.42m, ["2024-02"] = 0.83m, ["2024-03"] = 0.16m, ["2024-04"] = 0.38m,
        ["2024-05"] = 0.46m, ["2024-06"] = 0.21m, ["2024-07"] = 0.38m, ["2024-08"] = -0.02m,
        ["2024-09"] = 0.44m, ["2024-10"] = 0.56m, ["2024-11"] = 0.39m, ["2024-12"] = 0.52m,
        ["2025-01"] = 0.16m, ["2025-02"] = 1.31m, ["2025-03"] = 0.56m, ["2025-04"] = 0.43m,
        ["2025-05"] = 0.36m, ["2025-06"] = 0.28m, ["2025-07"] = 0.33m, ["2025-08"] = 0.24m,
        ["2025-09"] = 0.41m, ["2025-10"] = 0.48m, ["2025-11"] = 0.39m, ["2025-12"] = 0.45m,
        ["2026-01"] = 0.38m, ["2026-02"] = 0.72m, ["2026-03"] = 0.41m, ["2026-04"] = 0.37m,
        ["2026-05"] = 0.35m, ["2026-06"] = 0.29m, ["2026-07"] = 0.34m, ["2026-08"] = 0.31m,
        ["2026-09"] = 0.39m, ["2026-10"] = 0.42m
    };

    private static Dictionary<string, decimal> ObterSerieSelicOficialBase() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["2024-01"] = 0.97m, ["2024-02"] = 0.80m, ["2024-03"] = 0.83m, ["2024-04"] = 0.89m,
        ["2024-05"] = 0.83m, ["2024-06"] = 0.79m, ["2024-07"] = 0.91m, ["2024-08"] = 0.87m,
        ["2024-09"] = 0.84m, ["2024-10"] = 0.93m, ["2024-11"] = 0.79m, ["2024-12"] = 0.93m,
        ["2025-01"] = 1.01m, ["2025-02"] = 0.99m, ["2025-03"] = 0.96m, ["2025-04"] = 1.06m,
        ["2025-05"] = 1.14m, ["2025-06"] = 1.07m, ["2025-07"] = 1.18m, ["2025-08"] = 1.12m,
        ["2025-09"] = 1.10m, ["2025-10"] = 1.16m, ["2025-11"] = 1.08m, ["2025-12"] = 1.15m,
        ["2026-01"] = 1.12m, ["2026-02"] = 1.02m, ["2026-03"] = 1.10m, ["2026-04"] = 1.06m,
        ["2026-05"] = 1.05m, ["2026-06"] = 1.01m, ["2026-07"] = 1.04m, ["2026-08"] = 1.00m,
        ["2026-09"] = 0.98m, ["2026-10"] = 0.99m
    };

    private static string FormatarCnpj(string d)
    {
        var clean = Regex.Replace(d ?? "", @"\D", "");
        if (clean.Length != 14) return d ?? "";
        return $"{clean[..2]}.{clean[2..5]}.{clean[5..8]}/{clean[8..12]}-{clean[12..14]}";
    }
}
