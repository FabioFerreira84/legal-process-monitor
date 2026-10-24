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

    public MagistradosController(IMagistradoRepository repository)
    {
        _repository = repository;
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
    /// Obtém o dossiê de um magistrado pelo nome ou título (ex: 'Pedro Paulo Maillet Preuss' ou 'Alex Freitas Lima').
    /// </summary>
    [HttpGet("busca")]
    public async Task<ActionResult<PerfilMagistrado>> BuscarPorNome([FromQuery] string nome, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            return BadRequest(new { erro = "Parâmetro 'nome' é obrigatório." });
        }

        var magistrado = await _repository.ObterPorNomeAsync(nome, ct);
        if (magistrado == null)
        {
            return NotFound(new { mensagem = $"Dossiê do magistrado '{nome}' não localizado." });
        }

        return Ok(magistrado);
    }
}
