using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;
using LegalProcessMonitor.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace LegalProcessMonitor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProcessosController : ControllerBase
{
    private readonly IEnumerable<ITribunalConnector> _connectors;
    private readonly IEnumerable<ITribunalDocumentScraper> _documentScrapers;
    private readonly IProcessoRepository _repository;

    public ProcessosController(
        IEnumerable<ITribunalConnector> connectors,
        IEnumerable<ITribunalDocumentScraper> documentScrapers,
        IProcessoRepository repository)
    {
        _connectors = connectors;
        _documentScrapers = documentScrapers;
        _repository = repository;
    }

    /// <summary>
    /// Consulta um processo pelo número CNJ em formato com ou sem máscara.
    /// Busca primeiro no cache/MongoDB e, caso solicitado ou ausente, sincroniza diretamente com o tribunal.
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

        // Busca conector compatível
        var conector = ObterConectorParaTribunal(cnj);
        if (conector == null)
        {
            return StatusCode(501, new { erro = $"Nenhum conector disponível para o tribunal '{cnj.SiglaTribunal}'." });
        }

        var processoAtualizado = await conector.ConsultarProcessoAsync(cnj, ct);
        if (processoAtualizado == null)
        {
            return NotFound(new { mensagem = $"Processo '{cnj.NumeroFormatado}' não foi localizado no tribunal '{cnj.SiglaTribunal}'." });
        }

        // Também tenta capturar documentos públicos via scraper
        var scraper = ObterScraperParaTribunal(cnj);
        if (scraper != null)
        {
            var docs = await scraper.ObterDocumentosPublicosAsync(cnj, ct);
            if (docs != null && docs.Count > 0)
            {
                processoAtualizado.DocumentosPublicos = new List<DocumentoProcessual>(docs);
            }
        }

        await _repository.SalvarOuAtualizarAsync(processoAtualizado, ct);
        return Ok(processoAtualizado);
    }

    /// <summary>
    /// Obtém exclusivamente os documentos e julgamentos públicos capturados do tribunal.
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

        var scraper = ObterScraperParaTribunal(cnj);
        if (scraper == null)
        {
            return StatusCode(501, new { erro = $"Nenhum scraper de documentos disponível para o tribunal '{cnj.SiglaTribunal}'." });
        }

        var docs = await scraper.ObterDocumentosPublicosAsync(cnj, ct);
        if (processo != null)
        {
            processo.DocumentosPublicos = new List<DocumentoProcessual>(docs);
            await _repository.SalvarOuAtualizarAsync(processo, ct);
        }

        return Ok(docs);
    }

    /// <summary>
    /// Lista os processos recentemente monitorados ou consultados.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Processo>>> ListarRecentes(
        [FromQuery] int limite = 20,
        CancellationToken ct = default)
    {
        var processos = await _repository.ListarRecentesAsync(limite, ct);
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
