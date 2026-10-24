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
            Comarca = "Comarca de Guarulhos / Foro de Guarulhos / 1ª Vara Cível",
            Foro = "1ª Vara Cível do Foro de Guarulhos (Proc. 1º Grau: 1006805-85.2025.8.26.0224 - Juiz: Alex Freitas Lima)",
            ProcessoOrigem = "1006805-85.2025.8.26.0224",
            JuizOrigem = "Alex Freitas Lima",
            Relator = "PEDRO PAULO MAILLET PREUSS",
            ValorCausa = 10000.00m,
            SegredoJustica = false,
            DataDistribuicao = new System.DateTime(2025, 4, 7, 12, 21, 17, System.DateTimeKind.Utc),
            Status = "Julgado com Acórdão Proferido",
            UltimaAtualizacao = System.DateTime.UtcNow,
            Envolvidos = new List<Envolvido>
            {
                new Envolvido
                {
                    Nome = "Espólio de Paschoal Thomeu",
                    TipoPolo = "Ativo",
                    Papel = "Agravante",
                    Oab = "João Paulo Paiva Camacho - OAB 238470SP"
                },
                new Envolvido
                {
                    Nome = "Roseli Thomeu",
                    TipoPolo = "Ativo",
                    Papel = "Agravante",
                    Oab = "João Paulo Paiva Camacho - OAB 238470SP"
                },
                new Envolvido
                {
                    Nome = "Maria Aparecida da Silva Batista",
                    TipoPolo = "Passivo",
                    Papel = "Agravada",
                    Oab = "Regina Celia do Carmo de Luca - OAB 84842RJ; Jose Gabriel Meritello do Carmo de Andrade Araujo - OAB 510501SP"
                },
                new Envolvido
                {
                    Nome = "Arão Batista Antônio",
                    TipoPolo = "Passivo",
                    Papel = "Agravado",
                    Oab = "Regina Celia do Carmo de Luca - OAB 84842RJ; Jose Gabriel Meritello do Carmo de Andrade Araujo - OAB 510501SP"
                },
                new Envolvido
                {
                    Nome = "Life Empreendimentos Ltda.",
                    TipoPolo = "Passivo",
                    Papel = "Agravada",
                    Oab = "Marcio Rogerio dos Santos Dias - OAB 131627SP; Marcelo Oliveira dos Santos - OAB 278202SP; Rafael Almeida Freire - OAB 529240SP"
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

        // Processo 2: TRF3 Mandado de Segurança Cível
        var procTrf3 = new Processo
        {
            Id = "8532880c-4e1f-4416-8550-49d3fe9930d4",
            NumeroCnjFormatado = "5000001-05.2023.4.03.6100",
            NumeroCnjLimpo = "50000010520234036100",
            Tribunal = "TRF3",
            Grau = 1,
            Classe = "Mandado de Segurança Cível",
            Assunto = "Inquérito / Processo / Recurso Administrativo",
            OrgaoJulgador = "21ª VARA FEDERAL CÍVEL DE SÃO PAULO",
            Comarca = "Seção Judiciária de São Paulo (Federal)",
            Foro = "Foro Central Federal de São Paulo (6100)",
            Relator = "Juiz Federal Titular da 21ª Vara Federal",
            ValorCausa = 1000.00m,
            SegredoJustica = false,
            DataDistribuicao = new System.DateTime(2023, 1, 2, 0, 54, 32, System.DateTimeKind.Utc),
            Status = "Baixado Definitivamente (Transitado em Julgado)",
            UltimaAtualizacao = System.DateTime.UtcNow,
            Envolvidos = new List<Envolvido>
            {
                new Envolvido
                {
                    Nome = "Polo Ativo (Impetrante sob Proteção LGPD - Resolução CNJ nº 334/2020)",
                    TipoPolo = "Ativo",
                    Papel = "Impetrante",
                    Oab = "Patrono Habilitado no PJe TRF3"
                },
                new Envolvido
                {
                    Nome = "Delegado da Receita Federal do Brasil em São Paulo",
                    TipoPolo = "Passivo",
                    Papel = "Autoridade Coatora",
                    Oab = "Procuradoria-Geral da Fazenda Nacional (PGFN)"
                }
            },
            DocumentosPublicos = new List<DocumentoProcessual>
            {
                new DocumentoProcessual
                {
                    Id = "trf3-sentenca-001",
                    Tipo = "Sentença / Extinção sem Resolução do Mérito",
                    Data = new System.DateTime(2023, 3, 16, 17, 25, 22),
                    Titulo = "Sentença Extintiva - Art. 485, IV do CPC",
                    Situacao = "Transitado em Julgado",
                    MagistradoRelator = "Juiz Federal da 21ª Vara Federal Cível",
                    Ementa = "MANDADO DE SEGURANÇA – AUSÊNCIA DE PROVA PRÉ-CONSTITUÍDA DE DIREITO LÍQUIDO E CERTO – DILAÇÃO PROBATÓRIA INCOMPATÍVEL COM O RITO ESPECIAL DO MANDAMUS (ART. 6º DA LEI 12.016/2009) – INDEFERIMENTO DA INICIAL E EXTINÇÃO SEM RESOLUÇÃO DO MÉRITO.",
                    ConteudoTexto = "Julgo extinto o processo sem julgamento de mérito por ausência das condições da ação, com fulcro no art. 485, IV do Código de Processo Civil.",
                    CertidaoPublicacao = "Disponibilizado no Diário da Justiça Eletrônico do TRF3 em 17/03/2023. Trânsito em julgado certificado em 29/05/2023.",
                    UrlOrigem = "https://pje1g.trf3.jus.br"
                }
            }
        };

        var eventosTrf3 = new (string data, string titulo, string desc, int cod)[]
        {
            ("2023-05-30T16:53:59Z", "Baixa Definitiva", "Baixa Definitiva e Arquivamento dos Autos", 22),
            ("2023-05-29T11:17:03Z", "Trânsito em julgado", "Certificado o Trânsito em Julgado da Sentença", 848),
            ("2023-03-20T07:00:00Z", "Publicação", "Publicado no Diário Oficial Eletrônico da Justiça Federal", 92),
            ("2023-03-16T17:25:22Z", "Ausência das condições da ação", "Sentença Proferida: Extinção sem Resolução do Mérito", 461),
            ("2023-01-10T14:56:40Z", "Requisição de Informações", "Notificada a Autoridade Coatora para prestar informações em 10 dias", 11020),
            ("2023-01-02T00:54:32Z", "Distribuição", "Distribuído por sorteio à 21ª Vara Federal Cível de São Paulo", 26)
        };

        foreach (var (data, titulo, desc, cod) in eventosTrf3)
        {
            if (System.DateTime.TryParse(data, out var dt))
            {
                procTrf3.Movimentacoes.Add(new Movimentacao
                {
                    DataHora = dt,
                    Titulo = titulo,
                    Descricao = desc,
                    CodigoNacional = cod
                });
            }
        }

        _processos.TryAdd(procTrf3.NumeroCnjLimpo, procTrf3);
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
