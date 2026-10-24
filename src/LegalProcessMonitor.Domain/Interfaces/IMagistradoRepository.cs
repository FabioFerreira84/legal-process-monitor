using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;

namespace LegalProcessMonitor.Domain.Interfaces;

public interface IMagistradoRepository
{
    Task<IReadOnlyList<PerfilMagistrado>> ListarTodosAsync(CancellationToken ct = default);
    Task<PerfilMagistrado?> ObterPorNomeAsync(string nome, CancellationToken ct = default);
    Task<PerfilMagistrado?> ObterPorVaraOuCamaraAsync(string tribunal, string varaOuCamara, CancellationToken ct = default);
}
