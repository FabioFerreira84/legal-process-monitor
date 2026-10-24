using System;
using System.Collections.Generic;
using LegalProcessMonitor.Domain.ValueObjects;

namespace LegalProcessMonitor.Domain.Entities;

public class Processo
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string NumeroCnjFormatado { get; set; } = string.Empty;
    public string NumeroCnjLimpo { get; set; } = string.Empty;
    public string Tribunal { get; set; } = string.Empty;
    public int Grau { get; set; } = 1;
    public string? Classe { get; set; }
    public string? Assunto { get; set; }
    public string? OrgaoJulgador { get; set; }
    public DateTime? DataDistribuicao { get; set; }
    public string Status { get; set; } = "Em Andamento";
    public DateTime UltimaAtualizacao { get; set; } = DateTime.UtcNow;
    public string? Comarca { get; set; }
    public string? Foro { get; set; }
    public decimal? ValorCausa { get; set; }
    public string? Relator { get; set; }
    public bool SegredoJustica { get; set; } = false;

    public List<Movimentacao> Movimentacoes { get; set; } = new();
    public List<Envolvido> Envolvidos { get; set; } = new();
    public List<DocumentoProcessual> DocumentosPublicos { get; set; } = new();

    public static Processo Criar(NumeroProcessoCnj cnj, string? classe = null, string? assunto = null)
    {
        return new Processo
        {
            NumeroCnjFormatado = cnj.NumeroFormatado,
            NumeroCnjLimpo = cnj.ApenasDigitos,
            Tribunal = cnj.SiglaTribunal,
            Classe = classe,
            Assunto = assunto,
            UltimaAtualizacao = DateTime.UtcNow
        };
    }

    public void AdicionarMovimentacao(Movimentacao mov)
    {
        // Evita duplicidade de movimentações idênticas na mesma data/hora
        if (!Movimentacoes.Exists(m => m.DataHora == mov.DataHora && m.Titulo == mov.Titulo))
        {
            Movimentacoes.Insert(0, mov); // Mais recente no topo
            UltimaAtualizacao = DateTime.UtcNow;
        }
    }
}
