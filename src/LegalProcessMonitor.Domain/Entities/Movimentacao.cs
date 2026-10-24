using System;
using System.Collections.Generic;

namespace LegalProcessMonitor.Domain.Entities;

public class Movimentacao
{
    public DateTime DataHora { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public int? CodigoNacional { get; set; }
    public Dictionary<string, string> Complementos { get; set; } = new();
}
