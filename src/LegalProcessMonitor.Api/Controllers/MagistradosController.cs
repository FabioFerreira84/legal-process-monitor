using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LegalProcessMonitor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MagistradosController : ControllerBase
{
    private readonly IMagistradoRepository _repository;
    private readonly IInteligenciaPublicaConnector? _inteligenciaConnector;

    public MagistradosController(
        IMagistradoRepository repository,
        IInteligenciaPublicaConnector? inteligenciaConnector = null)
    {
        _repository = repository;
        _inteligenciaConnector = inteligenciaConnector;
    }

    /// <summary>
    /// Lista todos os magistrados e órgãos mapeados com seus dossiês forenses.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PerfilMagistrado>>> Listar(CancellationToken ct = default)
    {
        var lista = await _repository.ListarTodosAsync(ct);
        return Ok(lista);
    }

    /// <summary>
    /// Obtém o dossiê de um magistrado pelo nome ou título e enriquece ao vivo com seus últimos julgamentos reais no DJEN/CJSG.
    /// </summary>
    [HttpGet("busca")]
    public async Task<ActionResult<PerfilMagistrado>> BuscarPorNome(
        [FromQuery] string nome,
        [FromQuery] string tribunal = "TJSP",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            return BadRequest(new { erro = "Parâmetro 'nome' é obrigatório." });
        }

        var magistrado = await _repository.ObterPorNomeAsync(nome, ct);
        if (magistrado == null)
        {
            magistrado = new PerfilMagistrado
            {
                Id = $"mag-{nome.Trim().ToLowerInvariant().Replace(' ', '-')}",
                Nome = nome.Trim(),
                TituloFormatado = nome.Trim(),
                Cargo = $"Magistrado(a) / Relatoria ({tribunal.ToUpperInvariant()})",
                Tribunal = tribunal.ToUpperInvariant(),
                ComarcaOuSecao = $"Jurisdição {tribunal.ToUpperInvariant()}",
                VaraOuCamara = "Órgão Julgador Competente",
                EmailGabinete = "Consultar Catálogo Institucional do Tribunal",
                TelefoneCartorio = "Consultar Lista Oficial do Tribunal",
                BalcaoVirtualUrl = tribunal.ToUpperInvariant() == "TRF3"
                    ? "https://www.trf3.jus.br/balcao-virtual"
                    : tribunal.ToUpperInvariant() == "STJ"
                        ? "https://www.stj.jus.br/sites/portalp/Processos/Balcao-Virtual"
                        : "https://www.tjsp.jus.br/BalcaoVirtual",
                HorarioAtendimento = "Expediente forense oficial",
                PerfilGeral = "Dossiê enriquecido em tempo real via DJEN/CNJ e CJSG",
                TendenciaLiminares = "Verifique abaixo os acórdãos e decisões reais recentemente publicados por este magistrado no DJEN/CJSG.",
                TendenciaJusticaGratuita = "Observância do Art. 99 do CPC/2015.",
                TendenciaHonorarios = "Observância do Art. 85 do CPC/2015."
            };
        }

        if (_inteligenciaConnector != null)
        {
            var julgs = await _inteligenciaConnector.BuscarJulgamentosReaisMagistradoAsync(
                magistrado.Nome,
                string.IsNullOrWhiteSpace(magistrado.Tribunal) ? tribunal : magistrado.Tribunal,
                ct);

            if (julgs.Count > 0)
            {
                magistrado.JulgamentosReaisRecentes = new List<JulgamentoRealMagistrado>(julgs);
                if (string.IsNullOrWhiteSpace(magistrado.PrecedenteMarcante))
                {
                    magistrado.PrecedenteMarcante = julgs[0].EmentaResumo;
                }
            }
        }

        return Ok(magistrado);
    }
}
