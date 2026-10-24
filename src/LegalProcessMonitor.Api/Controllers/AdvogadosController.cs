using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LegalProcessMonitor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdvogadosController : ControllerBase
{
    private readonly IEsajOabScraper _oabScraper;

    public AdvogadosController(IEsajOabScraper oabScraper)
    {
        _oabScraper = oabScraper;
    }

    /// <summary>
    /// Consulta todos os processos ativos vinculados ao número de OAB do advogado no e-SAJ (1º e 2º Grau).
    /// Suporta formatos como '238470SP', '238470', '238.470/SP'.
    /// </summary>
    [HttpGet("{numeroOab}")]
    public async Task<ActionResult<AdvogadoCarteira>> ConsultarCarteira(string numeroOab, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(numeroOab))
        {
            return BadRequest(new { erro = "Número de OAB é obrigatório." });
        }

        var carteira = await _oabScraper.ConsultarProcessosPorOabAsync(numeroOab, ct);
        return Ok(carteira);
    }
}
