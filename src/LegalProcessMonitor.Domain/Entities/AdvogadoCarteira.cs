using System;
using System.Collections.Generic;

namespace LegalProcessMonitor.Domain.Entities;

public class ProcessoResumo
{
    public string NumeroCnjFormatado { get; set; } = string.Empty;
    public string NumeroCnjLimpo { get; set; } = string.Empty;
    public string Tribunal { get; set; } = "TJSP";
    public int Grau { get; set; } = 1;
    public string Classe { get; set; } = string.Empty;
    public string? Foro { get; set; }
    public string? Vara { get; set; }
    public string? Papel { get; set; }
    public string? DataRecebimento { get; set; }
    public string? UrlOrigem { get; set; }
    public string? FonteDados { get; set; }
    public string? UltimaComunicacaoResumo { get; set; }
}

public class AdvogadoCarteira
{
    public string NumeroOab { get; set; } = string.Empty;
    public string? NomeAdvogado { get; set; }
    public string Tribunal { get; set; } = "DJEN/CNJ + TJSP";
    public int TotalProcessos => Processos.Count;
    public int TotalComunicacoesDjen { get; set; }
    public DateTime DataConsulta { get; set; } = DateTime.UtcNow;
    public List<string> FontesConsultadas { get; set; } = new();
    public List<string> AvisosOuErros { get; set; } = new();
    public List<ProcessoResumo> Processos { get; set; } = new();
}
