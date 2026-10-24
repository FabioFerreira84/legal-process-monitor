using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.ValueObjects;

namespace LegalProcessMonitor.Domain.Interfaces;

public interface ITribunalDocumentScraper
{
    string NomeTribunal { get; }
    bool SuportaTribunal(NumeroProcessoCnj numeroProcesso);
    Task<IReadOnlyList<DocumentoProcessual>> ObterDocumentosPublicosAsync(NumeroProcessoCnj numeroProcesso, CancellationToken ct = default);
}
