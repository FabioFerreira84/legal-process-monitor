using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;
using LegalProcessMonitor.Domain.ValueObjects;
using LegalProcessMonitor.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace LegalProcessMonitor.Infrastructure.Connectors.DataJud;

public class DataJudConnector : ITribunalConnector
{
    private readonly HttpClient _httpClient;
    private readonly DataJudSettings _settings;
    private readonly ILogger<DataJudConnector> _logger;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

    public string NomeConector => "DataJud_CNJ_Oficial";

    public DataJudConnector(
        HttpClient httpClient,
        IOptions<DataJudSettings> settings,
        ILogger<DataJudConnector> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        // Política de Resiliência com Polly (Exponential Backoff + Jitter)
        _retryPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => (int)r.StatusCode == 429 || ((int)r.StatusCode >= 500 && (int)r.StatusCode <= 599))
            .WaitAndRetryAsync(
                _settings.TentativasRetry,
                tentativa => TimeSpan.FromSeconds(Math.Pow(2, tentativa)),
                (outcome, timespan, retryAttempt, context) =>
                {
                    _logger.LogWarning(
                        "Falha transitória na API DataJud. Tentativa {Attempt} em {Delay}s. Status: {StatusCode}",
                        retryAttempt,
                        timespan.TotalSeconds,
                        outcome.Result?.StatusCode);
                });
    }

    public bool SuportaTribunal(NumeroProcessoCnj numeroProcesso)
    {
        // DataJud atende a grande maioria dos tribunais brasileiros estaduais e federais
        return !string.IsNullOrWhiteSpace(numeroProcesso.SiglaTribunal);
    }

    public async Task<Processo?> ConsultarProcessoAsync(NumeroProcessoCnj numeroProcesso, CancellationToken ct = default)
    {
        var siglaTribunal = numeroProcesso.SiglaTribunal.ToLowerInvariant();
        var endpoint = $"{_settings.BaseUrl.TrimEnd('/')}/api_publica_{siglaTribunal}/_search";

        var payload = new
        {
            query = new
            {
                match = new
                {
                    numeroProcesso = numeroProcesso.ApenasDigitos
                }
            }
        };

        var jsonString = JsonSerializer.Serialize(payload);

        _logger.LogInformation("Consultando processo {Numero} no tribunal {Sigla} via DataJud...", numeroProcesso.NumeroFormatado, siglaTribunal.ToUpper());

        using var response = await _retryPolicy.ExecuteAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(jsonString, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("APIKey", _settings.ApiKey);
            return await _httpClient.SendAsync(request, ct);
        });

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("DataJud respondeu com status {StatusCode} para o processo {Numero}", response.StatusCode, numeroProcesso.NumeroFormatado);
            return null;
        }

        var responseString = await response.Content.ReadAsStringAsync(ct);
        return ParseDataJudResponse(responseString, numeroProcesso);
    }

    private Processo? ParseDataJudResponse(string json, NumeroProcessoCnj cnj)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("hits", out var hitsProp) ||
            !hitsProp.TryGetProperty("hits", out var hitsArray) ||
            hitsArray.GetArrayLength() == 0)
        {
            return null;
        }

        var firstHit = hitsArray[0];
        if (!firstHit.TryGetProperty("_source", out var source))
            return null;

        var processo = Processo.Criar(cnj);

        if (source.TryGetProperty("classe", out var classeProp) && classeProp.TryGetProperty("nome", out var nomeClasse))
            processo.Classe = nomeClasse.GetString();

        if (source.TryGetProperty("orgaoJulgador", out var orgaoProp) && orgaoProp.TryGetProperty("nome", out var nomeOrgao))
            processo.OrgaoJulgador = nomeOrgao.GetString();

        if (source.TryGetProperty("dataAjuizamento", out var dataAjuizamentoProp) && dataAjuizamentoProp.TryGetDateTime(out var dtAjuiz))
            processo.DataDistribuicao = dtAjuiz;

        if (source.TryGetProperty("assuntos", out var assuntosArray) && assuntosArray.GetArrayLength() > 0)
        {
            var firstAssunto = assuntosArray[0];
            if (firstAssunto.TryGetProperty("nome", out var nomeAssunto))
                processo.Assunto = nomeAssunto.GetString();
        }

        // Parsing dos Movimentos (Andamentos)
        if (source.TryGetProperty("movimentos", out var movimentosArray))
        {
            foreach (var m in movimentosArray.EnumerateArray())
            {
                var mov = new Movimentacao();

                if (m.TryGetProperty("dataHora", out var dtHoraProp) && dtHoraProp.TryGetDateTime(out var dtHora))
                    mov.DataHora = dtHora;

                if (m.TryGetProperty("nome", out var nomeMov))
                    mov.Titulo = nomeMov.GetString() ?? "Movimentação";

                if (m.TryGetProperty("codigo", out var codMov) && codMov.TryGetInt32(out var codInt))
                    mov.CodigoNacional = codInt;

                // Captura complementos tabulares
                if (m.TryGetProperty("complementosTabelados", out var compArray))
                {
                    var sb = new StringBuilder();
                    foreach (var comp in compArray.EnumerateArray())
                    {
                        if (comp.TryGetProperty("descricao", out var desc))
                            sb.Append(desc.GetString()).Append(" ");
                    }
                    mov.Descricao = sb.ToString().Trim();
                }

                if (string.IsNullOrWhiteSpace(mov.Descricao))
                    mov.Descricao = mov.Titulo;

                processo.AdicionarMovimentacao(mov);
            }
        }

        return processo;
    }
}
