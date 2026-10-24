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
    private readonly IProcessoRepository _repository;

    public ProcessosController(
        IEnumerable<ITribunalConnector> connectors,
        IProcessoRepository repository)
    {
        _connectors = connectors;
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

        await _repository.SalvarOuAtualizarAsync(processoAtualizado, ct);
        return Ok(processoAtualizado);
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
}
