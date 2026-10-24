using System;
using System.Collections.Generic;

namespace LegalProcessMonitor.Domain.Entities;

public class PerfilMagistrado
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Nome { get; set; } = string.Empty;
    public string TituloFormatado { get; set; } = string.Empty; // Ex: "Des. Pedro Paulo Maillet Preuss"
    public string Cargo { get; set; } = string.Empty; // Juiz de Direito Titular, Desembargador, Juiz Federal
    public string Tribunal { get; set; } = string.Empty; // TJSP, TRF3
    public string ComarcaOuSecao { get; set; } = string.Empty;
    public string VaraOuCamara { get; set; } = string.Empty;
    public string? EmailGabinete { get; set; }
    public string? TelefoneCartorio { get; set; }
    public string? BalcaoVirtualUrl { get; set; }
    public string? HorarioAtendimento { get; set; }
    
    // Perfil Decisório e Jurimetria Comportamental
    public string PerfilGeral { get; set; } = "Moderado / Legalista";
    public string TendenciaLiminares { get; set; } = string.Empty;
    public string TendenciaJusticaGratuita { get; set; } = string.Empty;
    public string TendenciaHonorarios { get; set; } = string.Empty;
    public List<string> DicasPeticionamento { get; set; } = new();
    public string? PrecedenteMarcante { get; set; }
}
