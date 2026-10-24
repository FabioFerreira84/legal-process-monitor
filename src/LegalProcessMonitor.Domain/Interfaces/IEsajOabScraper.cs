using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;

namespace LegalProcessMonitor.Domain.Interfaces;

public interface IEsajOabScraper
{
    Task<AdvogadoCarteira> ConsultarProcessosPorOabAsync(string numeroOab, CancellationToken ct = default);
}
