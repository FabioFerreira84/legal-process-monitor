using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;

namespace LegalProcessMonitor.Infrastructure.Repositories;

public class InMemoryProcessoRepository : IProcessoRepository
{
    private readonly ConcurrentDictionary<string, Processo> _processos = new();

    public Task<Processo?> ObterPorNumeroAsync(string numeroCnjLimpo, CancellationToken ct = default)
    {
        _processos.TryGetValue(numeroCnjLimpo, out var processo);
        return Task.FromResult(processo);
    }

    public Task SalvarOuAtualizarAsync(Processo processo, CancellationToken ct = default)
    {
        _processos.AddOrUpdate(processo.NumeroCnjLimpo, processo, (_, _) => processo);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Processo>> ListarRecentesAsync(int limite = 50, CancellationToken ct = default)
    {
        IReadOnlyList<Processo> lista = _processos.Values
            .OrderByDescending(p => p.UltimaAtualizacao)
            .Take(limite)
            .ToList();

        return Task.FromResult(lista);
    }
}
