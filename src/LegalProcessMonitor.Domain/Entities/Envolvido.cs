namespace LegalProcessMonitor.Domain.Entities;

public class Envolvido
{
    public string Nome { get; set; } = string.Empty;
    public string TipoPolo { get; set; } = "Indefinido"; // Ativo, Passivo, Terceiro, Advogado
    public string? Papel { get; set; } // Agravante, Agravado, Autor, Réu, Advogado
    public string? Oab { get; set; }
    public string? Documento { get; set; }
}
