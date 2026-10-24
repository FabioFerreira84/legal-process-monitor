using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.ValueObjects;

namespace LegalProcessMonitor.Domain.Interfaces;

public interface IDjenComunicacaoConnector
{
    Task EnriquecerProcessoAsync(Processo processo, NumeroProcessoCnj numeroProcesso, CancellationToken ct = default);
    Task EnriquecerCarteiraPorOabAsync(AdvogadoCarteira carteira, string numeroInscricaoOab, string ufOab, CancellationToken ct = default);
}
