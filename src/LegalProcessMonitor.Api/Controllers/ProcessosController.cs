using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;
using LegalProcessMonitor.Domain.Services;
using LegalProcessMonitor.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace LegalProcessMonitor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProcessosController : ControllerBase
{
    private readonly IEnumerable<ITribunalConnector> _connectors;
    private readonly IEnumerable<ITribunalDocumentScraper> _documentScrapers;
    private readonly IDjenComunicacaoConnector _djenConnector;
    private readonly IProcessoRepository _repository;

    public ProcessosController(
        IEnumerable<ITribunalConnector> connectors,
        IEnumerable<ITribunalDocumentScraper> documentScrapers,
        IDjenComunicacaoConnector djenConnector,
        IProcessoRepository repository)
    {
        _connectors = connectors;
        _documentScrapers = documentScrapers;
        _djenConnector = djenConnector;
        _repository = repository;
    }

    /// <summary>
    /// Health check rápido para aquecimento silencioso (Cold Start wake-up).
    /// </summary>
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new { status = "online", service = "LegalProcessMonitor.Api", timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Calcula o termo fatal de um prazo processual em dias úteis segundo o CPC/2015, Lei 14.759/2023 e Resolução CNJ 455/2022 (DJEN).
    /// </summary>
    [HttpGet("calcular-prazo")]
    public ActionResult<CalculoPrazoForenseResult> CalcularPrazo(
        [FromQuery] string dataMarco,
        [FromQuery] int diasUteis = 15,
        [FromQuery] bool ehDisponibilizacaoDjen = true,
        [FromQuery] bool prazoEmDobro = false,
        [FromQuery] string tribunal = "TJSP")
    {
        if (!DateOnly.TryParseExact(dataMarco, new[] { "yyyy-MM-dd", "dd/MM/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
        {
            return BadRequest(new { erro = "Formato de data inválido. Utilize YYYY-MM-DD ou DD/MM/YYYY." });
        }

        if (diasUteis is < 1 or > 180)
        {
            return BadRequest(new { erro = "O prazo em dias úteis deve estar entre 1 e 180 dias." });
        }

        var resultado = CalculadoraPrazoForense.Calcular(data, diasUteis, ehDisponibilizacaoDjen, prazoEmDobro, tribunal);
        return Ok(resultado);
    }

    /// <summary>
    /// Consulta um processo pelo número CNJ em formato com ou sem máscara.
    /// Busca primeiro no cache/MongoDB e, caso solicitado ou ausente, sincroniza com DataJud, DJEN/CNJ e tribunal de origem.
    /// </summary>
    [HttpGet("{numeroProcesso}")]
    public async Task<ActionResult<Processo>> ObterProcesso(
        string numeroProcesso,
        [FromQuery] bool forcarAtualizacao = false,
        CancellationToken ct = default)
    {
        if (!NumeroProcessoCnj.TryParse(numeroProcesso, out var cnj) || cnj == null)
        {
            return BadRequest(new { erro = "Número de processo CNJ inválido. O formato deve ser NNNNNNN-DD.AAAA.J.TR.OOOO com dígito verificador módulo 97 válido." });
        }

        if (!forcarAtualizacao)
        {
            var existente = await _repository.ObterPorNumeroAsync(cnj.ApenasDigitos, ct);
            if (existente != null)
                return Ok(existente);
        }

        var conector = ObterConectorParaTribunal(cnj);
        if (conector == null)
        {
            return StatusCode(501, new { erro = $"Nenhum conector disponível para o tribunal '{cnj.SiglaTribunal}'." });
        }

        var processoAtualizado = await conector.ConsultarProcessoAsync(cnj, ct);

        // Enriquece com crawler do tribunal se aplicável (ex: e-SAJ TJSP)
        var scraper = ObterScraperParaTribunal(cnj);
        if (scraper != null)
        {
            processoAtualizado ??= Processo.Criar(cnj);
            await scraper.EnriquecerProcessoAsync(processoAtualizado, cnj, ct);
        }

        // Enriquece sempre com publicações, ementas e partes da API Oficial do DJEN/CNJ (comunicaapi.pje.jus.br)
        processoAtualizado ??= Processo.Criar(cnj);
        await _djenConnector.EnriquecerProcessoAsync(processoAtualizado, cnj, ct);

        if (processoAtualizado.Movimentacoes.Count == 0 && processoAtualizado.DocumentosPublicos.Count == 0)
        {
            return NotFound(new { mensagem = $"Processo '{cnj.NumeroFormatado}' não foi localizado nos índices públicos (DataJud / DJEN / {cnj.SiglaTribunal})." });
        }

        // Metadados objetivos derivados exclusivamente do código CNJ ou do órgão julgador real (sem inventar valores de causa)
        if (string.IsNullOrWhiteSpace(processoAtualizado.Foro))
        {
            processoAtualizado.Foro = cnj.ForoOrigem == "0000"
                ? $"Originário de 2º Grau ({cnj.SiglaTribunal})"
                : $"Unidade / Foro Código CNJ {cnj.ForoOrigem}";
        }

        if (processoAtualizado.Envolvidos == null || processoAtualizado.Envolvidos.Count == 0)
        {
            processoAtualizado.Envolvidos = new List<Envolvido>
            {
                new Envolvido
                {
                    Nome = "Polo Ativo (Omitido na consulta pública - Resolução CNJ nº 334/2020 LGPD)",
                    TipoPolo = "Ativo",
                    Papel = "Polo Ativo",
                    Oab = "Acesso restrito aos autos ou sem publicação no DJEN"
                },
                new Envolvido
                {
                    Nome = "Polo Passivo (Omitido na consulta pública - Resolução CNJ nº 334/2020 LGPD)",
                    TipoPolo = "Passivo",
                    Papel = "Polo Passivo",
                    Oab = "Acesso restrito aos autos ou sem publicação no DJEN"
                }
            };
        }

        await _repository.SalvarOuAtualizarAsync(processoAtualizado, ct);
        return Ok(processoAtualizado);
    }

    /// <summary>
    /// Obtém exclusivamente os documentos e julgamentos públicos capturados do DJEN e tribunal.
    /// </summary>
    [HttpGet("{numeroProcesso}/documentos")]
    public async Task<ActionResult<IReadOnlyList<DocumentoProcessual>>> ObterDocumentos(
        string numeroProcesso,
        CancellationToken ct = default)
    {
        if (!NumeroProcessoCnj.TryParse(numeroProcesso, out var cnj) || cnj == null)
        {
            return BadRequest(new { erro = "Número de processo CNJ inválido." });
        }

        var processo = await _repository.ObterPorNumeroAsync(cnj.ApenasDigitos, ct);
        if (processo != null && processo.DocumentosPublicos.Count > 0)
        {
            return Ok(processo.DocumentosPublicos);
        }

        var procTemp = processo ?? Processo.Criar(cnj);
        var scraper = ObterScraperParaTribunal(cnj);
        if (scraper != null)
        {
            await scraper.EnriquecerProcessoAsync(procTemp, cnj, ct);
        }
        await _djenConnector.EnriquecerProcessoAsync(procTemp, cnj, ct);

        if (processo != null && procTemp.DocumentosPublicos.Count > 0)
        {
            processo.DocumentosPublicos = new List<DocumentoProcessual>(procTemp.DocumentosPublicos);
            await _repository.SalvarOuAtualizarAsync(processo, ct);
        }

        return Ok(procTemp.DocumentosPublicos);
    }

    /// <summary>
    /// Lista os processos recentemente monitorados ou consultados.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Processo>>> ListarRecentes(
        [FromQuery] int limite = 20,
        CancellationToken ct = default)
    {
        var limiteSeguro = Math.Clamp(limite, 1, 100);
        var processos = await _repository.ListarRecentesAsync(limiteSeguro, ct);
        return Ok(processos);
    }

    private ITribunalConnector? ObterConectorParaTribunal(NumeroProcessoCnj cnj)
    {
        foreach (var connector in _connectors)
        {
            if (connector.SuportaTribunal(cnj))
                return connector;
        }
        return null;
    }

    private ITribunalDocumentScraper? ObterScraperParaTribunal(NumeroProcessoCnj cnj)
    {
        foreach (var scraper in _documentScrapers)
        {
            if (scraper.SuportaTribunal(cnj))
                return scraper;
        }
        return null;
    }
}
