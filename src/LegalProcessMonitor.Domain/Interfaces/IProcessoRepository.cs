using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;

namespace LegalProcessMonitor.Domain.Interfaces;

public interface IProcessoRepository
{
    Task<Processo?> ObterPorNumeroAsync(string numeroCnjLimpo, CancellationToken ct = default);
    Task SalvarOuAtualizarAsync(Processo processo, CancellationToken ct = default);
    Task<IReadOnlyList<Processo>> ListarRecentesAsync(int limite = 50, CancellationToken ct = default);
}
