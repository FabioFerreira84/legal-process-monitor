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

        // 2. Complementa com Scraper no Banco de Jurisprudência e-SAJ (CJSG) se for tribunal e-SAJ e ainda houver poucos resultados
        if (resultados.Count < 3 && (string.Equals(siglaTribunal, "TJSP", StringComparison.OrdinalIgnoreCase) || string.Equals(siglaTribunal, "TJMS", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var host = string.Equals(siglaTribunal, "TJMS", StringComparison.OrdinalIgnoreCase)
                    ? "https://esaj.tjms.jus.br"
                    : "https://esaj.tjsp.jus.br";

                var cjsgUrl = $"{host}/cjsg/resultadoCompleta.do?dados.buscaInteiroTeor={Uri.EscapeDataString(nomeLimpo)}&dados.origensSelecionadas=T";
                using var respCjsg = await _httpClient.GetAsync(cjsgUrl, ct);
                if (respCjsg.IsSuccessStatusCode)
                {
                    var html = await respCjsg.Content.ReadAsStringAsync(ct);
                    ExtrairJulgamentosDeHtmlCjsg(html, siglaTribunal.ToUpperInvariant(), host, resultados);
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

    private static string FormatarCnpj(string d)
    {
        var clean = Regex.Replace(d ?? "", @"\D", "");
        if (clean.Length != 14) return d ?? "";
        return $"{clean[..2]}.{clean[2..5]}.{clean[5..8]}/{clean[8..12]}-{clean[12..14]}";
    }
}
