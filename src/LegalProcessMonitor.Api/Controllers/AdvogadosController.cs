using System.Text.RegularExpressions;
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
    private static readonly Regex OabValidRegex = new(@"^[0-9.\-\/\s]{2,12}[a-zA-Z]{0,2}$", RegexOptions.Compiled);

    public AdvogadosController(IEsajOabScraper oabScraper)
    {
        _oabScraper = oabScraper;
    }

    /// <summary>
    /// Consulta a carteira de processos vinculados ao número de OAB do advogado via API Oficial do DJEN/CNJ e e-SAJ.
    /// Suporta formatos como '238470SP', '238470', '238.470/SP'.
    /// </summary>
    [HttpGet("{numeroOab}")]
    public async Task<ActionResult<AdvogadoCarteira>> ConsultarCarteira(string numeroOab, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(numeroOab) || !OabValidRegex.IsMatch(numeroOab.Trim()))
        {
            return BadRequest(new { erro = "Formato de OAB inválido. Informe apenas dígitos e a UF opcional (ex: 238470SP ou 238.470/SP)." });
        }

        var carteira = await _oabScraper.ConsultarProcessosPorOabAsync(numeroOab.Trim(), ct);
        return Ok(carteira);
    }
}
