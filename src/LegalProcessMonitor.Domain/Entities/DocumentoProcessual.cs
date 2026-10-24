using System;

namespace LegalProcessMonitor.Domain.Entities;

public class DocumentoProcessual
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Tipo { get; set; } = "Julgamento"; // Julgamento, Decisão, Despacho, Sentença
    public DateTime Data { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string ConteudoTexto { get; set; } = string.Empty;
    public string Situacao { get; set; } = string.Empty;
    public string? MagistradoRelator { get; set; }
    public string? UrlOrigem { get; set; }
}
