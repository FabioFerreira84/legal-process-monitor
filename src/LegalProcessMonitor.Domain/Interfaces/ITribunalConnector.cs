using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.ValueObjects;

namespace LegalProcessMonitor.Domain.Interfaces;

public interface ITribunalConnector
{
    string NomeConector { get; }
    bool SuportaTribunal(NumeroProcessoCnj numeroProcesso);
    Task<Processo?> ConsultarProcessoAsync(NumeroProcessoCnj numeroProcesso, CancellationToken ct = default);
}
