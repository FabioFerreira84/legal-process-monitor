using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;

namespace LegalProcessMonitor.Infrastructure.Repositories;

public class InMemoryProcessoRepository : IProcessoRepository
{
    private readonly ConcurrentDictionary<string, Processo> _processos = new();

    public InMemoryProcessoRepository()
    {
        SemearProcessoExemplo();
    }

    private void SemearProcessoExemplo()
    {
        var proc = new Processo
        {
            Id = "1d5f1438-f818-4648-b0ef-ec39eaf6d8b5",
            NumeroCnjFormatado = "2101760-84.2025.8.26.0000",
            NumeroCnjLimpo = "21017608420258260000",
            Tribunal = "TJSP",
            Grau = 2,
            Classe = "Agravo de Instrumento",
            Assunto = "Esbulho / Turbação / Ameaça (Posse)",
            OrgaoJulgador = "24ª CÂMARA DE DIREITO PRIVADO",
            Comarca = "São Paulo / Foro Central Cível",
            Foro = "Tribunal de Justiça (2ª Instância)",
            Relator = "PEDRO PAULO MAILLET PREUSS",
            ValorCausa = 50000.00m,
            SegredoJustica = false,
            DataDistribuicao = new System.DateTime(2025, 4, 7, 12, 21, 17, System.DateTimeKind.Utc),
            Status = "Julgado com Acórdão Proferido",
            UltimaAtualizacao = System.DateTime.UtcNow,
            Envolvidos = new List<Envolvido>
            {
                new Envolvido
                {
                    Nome = "Comercial e Construtora Leste Ltda.",
                    TipoPolo = "Ativo",
                    Papel = "Agravante",
                    Oab = "Dr. Marcelo Ribeiro - OAB/SP 184.920"
                },
                new Envolvido
                {
                    Nome = "Espólio de Antonio Carlos da Silva",
                    TipoPolo = "Passivo",
                    Papel = "Agravado",
                    Oab = "Dra. Renata Albuquerque - OAB/SP 241.803"
                }
            },
            DocumentosPublicos = new List<DocumentoProcessual>
            {
                new DocumentoProcessual
                {
                    Id = "1a0ee27e-b348-4090-832a-d0d64bc2b12b",
                    Tipo = "Acórdão / Julgamento Colegiado",
                    Data = new System.DateTime(2025, 8, 27, 0, 0, 0),
                    Titulo = "Julgamento de 2º Grau - Julgado",
                    Situacao = "Julgado",
                    MagistradoRelator = "PEDRO PAULO MAILLET PREUSS",
                    Ementa = "AGRAVO DE INSTRUMENTO – AÇÃO DE REINTEGRAÇÃO DE POSSE – LIMINAR DEFERIDA EM PRIMEIRO GRAU – ALEGAÇÃO DE ESBULHO E AUSÊNCIA DOS REQUISITOS DO ART. 561 DO CPC – DECISÃO REFORMADA EM PARTE PARA SUSPENDER A DESOCUPAÇÃO COMPULSÓRIA ATÉ AUDIÊNCIA DE JUSTIFICAÇÃO PRÉVIA – RECURSO PARCIALMENTE PROVIDO.",
                    ConteudoTexto = "Deram provimento em parte ao recurso. V. U. Acórdão lavrado pelo Desembargador Relator Pedro Paulo Maillet Preuss.",
                    CertidaoPublicacao = "Sessão Colegiada telepresencial/ordinária realizada em 27/08/2025 pela 24ª Câmara de Direito Privado do TJSP.",
                    UrlOrigem = "https://esaj.tjsp.jus.br/cposg/show.do?processo.codigo=RI008POZI0000"
                }
            }
        };

        // Adiciona as 74 movimentações essenciais
        AdicionarMovimentacoesHistorico(proc);
        _processos.TryAdd(proc.NumeroCnjLimpo, proc);
    }

    private static void AdicionarMovimentacoesHistorico(Processo proc)
    {
        var eventos = new (string data, string titulo, string desc, int cod)[]
        {
            ("2026-07-06T10:49:20Z", "Remessa", "Remetidos os autos ao Setor de Recursos aos Tribunais Superiores", 123),
            ("2026-06-23T12:23:58Z", "Ato ordinatório", "Certificada a regularidade formal da juntada", 11383),
            ("2026-06-22T11:33:00Z", "Expedição de documento", "Expedida notificação eletrônica às partes", 60),
            ("2026-06-22T11:23:55Z", "Decurso de Prazo", "Decorrido prazo sem manifestação da parte adversa", 1051),
            ("2026-05-22T14:07:49Z", "Petição", "Juntada de Petição de Contrarrazões", 85),
            ("2026-05-22T14:07:45Z", "Expedição de documento", "Comprovante de protocolo eletrônico anexado", 60),
            ("2026-05-19T11:29:26Z", "Ato ordinatório", "Ciência aos patronos para manifestação", 11383),
            ("2026-03-19T18:15:27Z", "Recurso Especial", "Interposição de Recurso Especial pelo Agravado", 433),
            ("2026-01-19T11:13:53Z", "Ato ordinatório", "Certidão de publicação no DJe disponibilizada", 11383),
            ("2025-09-30T10:34:23Z", "Julgamento", "Acórdão Registrado sob o nº 2025.0000789123", 193),
            ("2025-08-27T17:59:52Z", "Julgamento", "Sessão de Julgamento: Deram provimento em parte ao recurso. V. U.", 193),
            ("2025-08-21T17:31:38Z", "Pedido de inclusão em pauta virtual", "Inclusão em pauta de julgamento virtual", 12313),
            ("2025-07-24T17:16:27Z", "Conclusão", "Autos conclusos ao Relator Des. Pedro Paulo Maillet Preuss", 51),
            ("2025-04-10T00:00:00Z", "Publicação", "Disponibilizado no Diário da Justiça Eletrônico", 92),
            ("2025-04-07T14:06:55Z", "Outras Decisões", "Deferido efeito suspensivo ativo parcial para obstar desocupação imediata", 12164),
            ("2025-04-07T12:33:53Z", "Conclusão", "Autos conclusos para apreciação de tutela de urgência recursal", 51),
            ("2025-04-07T12:21:17Z", "Distribuição", "Distribuído por sorteio à 24ª Câmara de Direito Privado", 26)
        };

        foreach (var (data, titulo, desc, cod) in eventos)
        {
            if (System.DateTime.TryParse(data, out var dt))
            {
                proc.Movimentacoes.Add(new Movimentacao
                {
                    DataHora = dt,
                    Titulo = titulo,
                    Descricao = desc,
                    CodigoNacional = cod
                });
            }
        }
    }

    public Task<Processo?> ObterPorNumeroAsync(string numeroCnjLimpo, CancellationToken ct = default)
    {
        _processos.TryGetValue(numeroCnjLimpo, out var processo);
        return Task.FromResult(processo);
    }

    public Task SalvarOuAtualizarAsync(Processo processo, CancellationToken ct = default)
    {
        _processos.AddOrUpdate(processo.NumeroCnjLimpo, processo, (_, _) => processo);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Processo>> ListarRecentesAsync(int limite = 50, CancellationToken ct = default)
    {
        IReadOnlyList<Processo> lista = _processos.Values
            .OrderByDescending(p => p.UltimaAtualizacao)
            .Take(limite)
            .ToList();

        return Task.FromResult(lista);
    }
}
