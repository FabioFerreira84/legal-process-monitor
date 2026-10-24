namespace LegalProcessMonitor.Domain.Entities;

public class Envolvido
{
    public string Nome { get; set; } = string.Empty;
    public string TipoPolo { get; set; } = "Indefinido"; // Ativo, Passivo, Terceiro, Advogado
    public string? Documento { get; set; }
}
