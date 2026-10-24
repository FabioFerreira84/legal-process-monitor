using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;

namespace LegalProcessMonitor.Infrastructure.Repositories;

/// <summary>
/// Repositório de fichas institucionais de órgãos julgadores e relatorias.
/// Contém exclusivamente informações públicas verificáveis (links oficiais de Balcão Virtual,
/// normas regimentais/legais do rito e precedentes reais publicados no DJEN/CNJ), sem perfilamento subjetivo.
/// </summary>
public class InMemoryMagistradoRepository : IMagistradoRepository
{
    private readonly ConcurrentDictionary<string, PerfilMagistrado> _magistrados = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryMagistradoRepository()
    {
        SemearMagistrados();
    }

    private void SemearMagistrados()
    {
        // 1. Relatoria 24ª Câmara de Direito Privado (TJSP) - Des. Pedro Paulo Maillet Preuss
        var preuss = new PerfilMagistrado
        {
            Id = "mag-tjsp-preuss",
            Nome = "Pedro Paulo Maillet Preuss",
            TituloFormatado = "Des. Pedro Paulo Maillet Preuss",
            Cargo = "Desembargador Relator — 2º Grau TJSP",
            Tribunal = "TJSP",
            ComarcaOuSecao = "São Paulo — Páteo do Colégio, 73, 3º andar (Processamento 12º Grupo)",
            VaraOuCamara = "24ª Câmara de Direito Privado",
            EmailGabinete = "Consultar Catálogo Oficial de Unidades: tjsp.jus.br/ListaTelefonica",
            TelefoneCartorio = "(11) 3295-5600 (PABX Direito Privado - Páteo do Colégio)",
            BalcaoVirtualUrl = "https://www.tjsp.jus.br/BalcaoVirtual",
            HorarioAtendimento = "Segunda a Sexta, das 13h às 17h (Atendimento Público TJSP / Balcão Virtual)",
            PerfilGeral = "Competência Regimental: Direito Privado II e III (Possessórias, Contratos e Bancário — Res. TJSP 623/2013)",
            TendenciaLiminares = "Registro DJEN (ID 370489236): Em ações de manutenção/reintegração de posse, aplica entendimento jurisprudencial de que o valor da causa deve corresponder a 1/3 do valor venal do imóvel quando ausente critério expresso no art. 292 do CPC.",
            TendenciaJusticaGratuita = "Regra geral do CPC (Art. 99, §§ 2º e 3º): presunção relativa para pessoa natural, cabendo comprovação documental se houver elementos que evidenciem falta dos pressupostos.",
            TendenciaHonorarios = "Disciplina estrita do Art. 85, §§ 1º, 2º e 11 do CPC (majoração recursal quando cabível).",
            DicasPeticionamento = new List<string>
            {
                "Peticionamento exclusivamente eletrônico (Resolução TJSP nº 551/2011, art. 7º).",
                "Inscrição para sustentação oral ou preferência regimental deve observar antecedência prevista no Regimento Interno do TJSP (art. 146).",
                "Em embargos de declaração, indicar expressamente o ponto de omissão, contradição ou obscuridade (Art. 1.022 CPC) e o prequestionamento ficto (Art. 1.025 CPC)."
            },
            PrecedenteMarcante = "DJEN ID 370489236 (04/09/2025): AI 2101760-84.2025.8.26.0000 — Provimento parcial unânime (V.U.) fixando valor da causa em possessória em 1/3 do valor venal; DJEN ID 421853952 (02/10/2025): EDcl rejeitados (V.U.)."
        };

        // 2. 1ª Vara Cível da Comarca de Guarulhos (TJSP) - Juiz Dr. Alex Freitas Lima
        var alex = new PerfilMagistrado
        {
            Id = "mag-tjsp-alex-freitas",
            Nome = "Alex Freitas Lima",
            TituloFormatado = "Dr. Alex Freitas Lima",
            Cargo = "Juiz de Direito — 1º Grau TJSP",
            Tribunal = "TJSP",
            ComarcaOuSecao = "Comarca de Guarulhos — Foro de Guarulhos (Código CNJ 0224)",
            VaraOuCamara = "1ª Vara Cível de Guarulhos",
            EmailGabinete = "guarulhos1cv@tjsp.jus.br (Ofício da 1ª Vara Cível)",
            TelefoneCartorio = "Catálogo Oficial TJSP: tjsp.jus.br/ListaTelefonica",
            BalcaoVirtualUrl = "https://www.tjsp.jus.br/BalcaoVirtual",
            HorarioAtendimento = "Segunda a Sexta, das 13h às 17h via Balcão Virtual Oficial TJSP",
            PerfilGeral = "Competência Cível Residual e Possessória de 1ª Instância (Foro de Guarulhos)",
            TendenciaLiminares = "Rito das Ações Possessórias (Arts. 560 a 566 do CPC): apreciação dos requisitos objetivos do Art. 561 do CPC (posse, turbação/esbulho, data e continuação/perda da posse).",
            TendenciaJusticaGratuita = "Observância do Art. 99 do CPC/2015.",
            TendenciaHonorarios = "Fixação na sentença conforme parâmetros do Art. 85, § 2º do CPC (10% a 20% sobre o valor da condenação ou proveito econômico).",
            DicasPeticionamento = new List<string>
            {
                "Instruir iniciais possessórias com prova documental datada da posse e da turbação/esbulho (Art. 561 CPC).",
                "Conferir o valor atribuído à causa segundo a jurisprudência da Câmara competente (1/3 do valor venal em disputas possessórias).",
                "Para atendimento pelo cartório ou gabinete, utilizar o link oficial do Balcão Virtual do TJSP selecionando a Comarca de Guarulhos -> 1ª Vara Cível."
            },
            PrecedenteMarcante = "Autos de Origem nº 1006805-85.2025.8.26.0224 (Ação de Reintegração / Manutenção de Posse — 1ª Vara Cível de Guarulhos)."
        };

        // 3. 21ª Vara Federal Cível de São Paulo (TRF3)
        var juizFederal = new PerfilMagistrado
        {
            Id = "mag-trf3-21cv",
            Nome = "Juiz Federal da 21ª Vara Federal Cível",
            TituloFormatado = "Juízo Titular da 21ª Vara Federal Cível de SP",
            Cargo = "Juízo Federal de 1º Grau — 1ª Subseção Judiciária SP",
            Tribunal = "TRF3",
            ComarcaOuSecao = "Seção Judiciária de São Paulo (Código CNJ 6100)",
            VaraOuCamara = "21ª Vara Federal Cível de São Paulo",
            EmailGabinete = "Consultar Catálogo TRF3: trf3.jus.br/balcao-virtual",
            TelefoneCartorio = "(11) 2172-6200 (Fórum Pedro Lessa - Av. Paulista, 1682)",
            BalcaoVirtualUrl = "https://www.trf3.jus.br/balcao-virtual",
            HorarioAtendimento = "Segunda a Sexta, das 12h às 19h via Balcão Virtual do TRF3",
            PerfilGeral = "Competência Cível e Tributária Federal (Art. 109, I e VIII da Constituição Federal)",
            TendenciaLiminares = "Mandado de Segurança (Lei 12.016/2009, art. 6º e 7º, III): exige prova documental pré-constituída de direito líquido e certo, vedada dilação probatória.",
            TendenciaJusticaGratuita = "Apreciação nos termos do Art. 99 do CPC e normativas da Justiça Federal da 3ª Região.",
            TendenciaHonorarios = "Em Mandado de Segurança não há condenação em honorários advocatícios (Art. 25 da Lei 12.016/2009, Súmula 512 do STF e Súmula 105 do STJ).",
            DicasPeticionamento = new List<string>
            {
                "No rito do Mandado de Segurança, anexar na petição inicial a íntegra do processo administrativo ou fiscal que comprova o ato coator (Art. 6º Lei 12.016/2009).",
                "Atentar à prerrogativa de prazo em dobro para a União / Fazenda Nacional (Art. 183 do CPC) e intimação pessoal via PJe.",
                "Atendimento telepresencial disponível no portal oficial do Balcão Virtual do TRF3."
            },
            PrecedenteMarcante = "MS nº 5000001-05.2023.4.03.6100 (Sentença extintiva sem resolução do mérito por ausência de prova pré-constituída — Art. 485, IV do CPC)."
        };

        // 4. Superior Tribunal de Justiça (STJ) - Min. Antonio Carlos Ferreira (4ª Turma / 2ª Seção)
        var stjRelator = new PerfilMagistrado
        {
            Id = "mag-stj-antonio-carlos",
            Nome = "Antonio Carlos Ferreira",
            TituloFormatado = "Min. Antonio Carlos Ferreira",
            Cargo = "Ministro do Superior Tribunal de Justiça (STJ)",
            Tribunal = "STJ",
            ComarcaOuSecao = "Brasília/DF — Superior Tribunal de Justiça (2ª Seção / 4ª Turma - Direito Privado)",
            VaraOuCamara = "Quarta Turma — Direito Privado (STJ)",
            EmailGabinete = "Portal de Atendimento STJ: stj.jus.br",
            TelefoneCartorio = "(61) 3319-8000 (Central de Atendimento STJ)",
            BalcaoVirtualUrl = "https://www.stj.jus.br/sites/portalp/Processos/Balcao-Virtual",
            HorarioAtendimento = "Segunda a Sexta, das 13h às 19h (Horário de Brasília)",
            PerfilGeral = "Competência Constitucional: Uniformização da Interpretação da Lei Federal (Art. 105, III da CF — Direito Privado)",
            TendenciaLiminares = "Concessão de efeito suspensivo a Recurso Especial ou AREsp exige demonstração cumulativa de periculum in mora e forte plausibilidade de êxito recursal (Art. 1.029, § 5º CPC).",
            TendenciaJusticaGratuita = "Observância da jurisprudência da Corte Especial do STJ e comprovação do preparo recursal (Art. 1.007 CPC) quando não deferida a gratuidade.",
            TendenciaHonorarios = "Majoração de honorários recursais nos termos do Tema Repetitivo 1.059/STJ e Art. 85, § 11 do CPC.",
            DicasPeticionamento = new List<string>
            {
                "No Agravo em Recurso Especial (AREsp), impugnar especificamente todos os fundamentos da decisão de inadmissibilidade da Presidência do Tribunal de origem (Súmula 182/STJ e Art. 932, III CPC).",
                "Demonstrar o prequestionamento da matéria federal (Súmulas 282/STF e 211/STJ, observado o Art. 1.025 do CPC) e a não incidência da Súmula 7/STJ (reexame fático-probatório)."
            },
            PrecedenteMarcante = "DJEN ID 765092040 (23/10/2026): AREsp 3319477/SP (2026/0275833-5) — Distribuído à relatoria do Min. Antonio Carlos Ferreira nos autos originários 2101760-84.2025.8.26.0000."
        };

        _magistrados.TryAdd(preuss.Nome, preuss);
        _magistrados.TryAdd(alex.Nome, alex);
        _magistrados.TryAdd(juizFederal.Nome, juizFederal);
        _magistrados.TryAdd(stjRelator.Nome, stjRelator);
    }

    public Task<IReadOnlyList<PerfilMagistrado>> ListarTodosAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<PerfilMagistrado>>(_magistrados.Values.ToList());
    }

    public Task<PerfilMagistrado?> ObterPorNomeAsync(string nome, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nome)) return Task.FromResult<PerfilMagistrado?>(null);

        var normalizado = nome.ToLowerInvariant();
        var resultado = _magistrados.Values.FirstOrDefault(m =>
            normalizado.Contains(m.Nome.ToLowerInvariant()) ||
            m.Nome.ToLowerInvariant().Contains(normalizado) ||
            (m.TituloFormatado != null && m.TituloFormatado.ToLowerInvariant().Contains(normalizado)));

        return Task.FromResult(resultado);
    }

    public Task<PerfilMagistrado?> ObterPorVaraOuCamaraAsync(string tribunal, string varaOuCamara, CancellationToken ct = default)
    {
        var tribNorm = (tribunal ?? "").ToLowerInvariant();
        var varaNorm = (varaOuCamara ?? "").ToLowerInvariant();

        var resultado = _magistrados.Values.FirstOrDefault(m =>
            m.Tribunal.ToLowerInvariant() == tribNorm &&
            (m.VaraOuCamara.ToLowerInvariant().Contains(varaNorm) || varaNorm.Contains(m.VaraOuCamara.ToLowerInvariant())));

        return Task.FromResult(resultado);
    }
}
