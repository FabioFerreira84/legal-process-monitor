using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;

namespace LegalProcessMonitor.Infrastructure.Repositories;

public class InMemoryMagistradoRepository : IMagistradoRepository
{
    private readonly ConcurrentDictionary<string, PerfilMagistrado> _magistrados = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryMagistradoRepository()
    {
        SemearMagistrados();
    }

    private void SemearMagistrados()
    {
        // 1. Desembargador Pedro Paulo Maillet Preuss (TJSP - 24ª Câmara)
        var preuss = new PerfilMagistrado
        {
            Id = "mag-tjsp-preuss",
            Nome = "Pedro Paulo Maillet Preuss",
            TituloFormatado = "Des. Pedro Paulo Maillet Preuss",
            Cargo = "Desembargador do Tribunal de Justiça",
            Tribunal = "TJSP",
            ComarcaOuSecao = "São Paulo / Tribunal de Justiça",
            VaraOuCamara = "24ª Câmara de Direito Privado",
            EmailGabinete = "gabinete.des.preuss@tjsp.jus.br",
            TelefoneCartorio = "(11) 3295-5600",
            BalcaoVirtualUrl = "https://tjsp.jus.br/balcaovirtual",
            HorarioAtendimento = "Segunda a Sexta, das 13h às 18h (mediante agendamento)",
            PerfilGeral = "Garantista / Cautelar / Racionalidade Econômica",
            TendenciaLiminares = "Elevada cautela em medidas desocupatórias. Em reintegração de posse e tutela possessória, costuma suspender desocupação liminar compulsória para prévia justificação (Art. 562 CPC).",
            TendenciaJusticaGratuita = "Exige comprovação objetiva quando impugnada ou se houver indícios de capacidade financeira declarada nos autos.",
            TendenciaHonorarios = "Aplica estritamente as balizas do Art. 85, § 2º do CPC, evitando arbitramento por equidade fora das hipóteses do § 8º.",
            DicasPeticionamento = new List<string>
            {
                "Memoriais recursais devem ser sucintos (máximo 4 a 5 laudas), destacando os trechos da decisão agravada.",
                "Sustentação oral presencial ou telepresencial é bem acolhida mediante inscrição tempestiva até 48h antes da sessão da 24ª Câmara.",
                "Valoriza precedentes consolidados das Turmas de Direito Privado e súmulas do STJ."
            },
            PrecedenteMarcante = "AI 2101760-84.2025.8.26.0000 (Provimento Parcial unânime para suspender desocupação até audiência de justificação prévia)."
        };

        // 2. Juiz de Direito Alex Freitas Lima (TJSP - 1ª Vara Cível de Guarulhos)
        var alex = new PerfilMagistrado
        {
            Id = "mag-tjsp-alex-freitas",
            Nome = "Alex Freitas Lima",
            TituloFormatado = "Dr. Alex Freitas Lima",
            Cargo = "Juiz de Direito Titular",
            Tribunal = "TJSP",
            ComarcaOuSecao = "Comarca de Guarulhos",
            VaraOuCamara = "1ª Vara Cível de Guarulhos",
            EmailGabinete = "guarulhos1cv@tjsp.jus.br",
            TelefoneCartorio = "(11) 2463-5401",
            BalcaoVirtualUrl = "https://tjsp.jus.br/balcaovirtual",
            HorarioAtendimento = "Segunda a Sexta, das 13h às 17h via Balcão Virtual",
            PerfilGeral = "Pragmático / Eficiência Processual e Gestão Ativa de Autos",
            TendenciaLiminares = "Analisa detalhadamente a plausibilidade inicial do direito (fumus boni iuris). Em ações possessórias, exige demonstração cabal do início do esbulho.",
            TendenciaJusticaGratuita = "Determina juntada de IRPF e holerite/extrato bancário antes de apreciar pedido de gratuidade se houver contratação de patrono particular sem declaração de pobreza estrita.",
            TendenciaHonorarios = "Segue parametrização padrão do tribunal.",
            DicasPeticionamento = new List<string>
            {
                "Priorizar pedidos pontuais e capítulos de sentença bem destacados na petição inicial.",
                "Para despacho urgente de tutela, entrar no Balcão Virtual da 1ª Vara Cível no início da tarde com o número dos autos e certidão do plantão/distribuição.",
                "Gabinete célere: movimentações com despacho costumam ocorrer em menos de 10 dias úteis."
            },
            PrecedenteMarcante = "Autos de Origem nº 1006805-85.2025.8.26.0224 (1ª Vara Cível de Guarulhos)."
        };

        // 3. Juiz Federal da 21ª Vara Federal Cível de SP (TRF3)
        var juizFederal = new PerfilMagistrado
        {
            Id = "mag-trf3-21cv",
            Nome = "Juiz Federal da 21ª Vara Federal Cível",
            TituloFormatado = "MM. Juiz Federal Titular",
            Cargo = "Juiz Federal Titular",
            Tribunal = "TRF3",
            ComarcaOuSecao = "Seção Judiciária de São Paulo (Federal)",
            VaraOuCamara = "21ª Vara Federal Cível de São Paulo",
            EmailGabinete = "cive-vara21@trf3.jus.br",
            TelefoneCartorio = "(11) 2172-6321",
            BalcaoVirtualUrl = "https://www.trf3.jus.br/balcao-virtual",
            HorarioAtendimento = "Segunda a Sexta, das 12h às 19h via Balcão Virtual do TRF3",
            PerfilGeral = "Rigor Formal / Rito Especial e Exegese Tributária",
            TendenciaLiminares = "Exige prova pré-constituída documental integral em Mandados de Segurança. Indeferimento liminar se houver qualquer necessidade de dilação probatória.",
            TendenciaJusticaGratuita = "Apreciação de acordo com a faixa isenta da Receita Federal.",
            TendenciaHonorarios = "Em Mandado de Segurança não cabem honorários (Súmula 512 STF e 105 STJ; Art. 25 Lei 12.016/2009).",
            DicasPeticionamento = new List<string>
            {
                "Em Mandado de Segurança, juntar todos os comprovantes fiscais e administrativos numerados como documentos essenciais.",
                "Despachos de liminares tributárias exigem memória de cálculo demonstrativa da exigibilidade suspensa (Art. 151 CTN).",
                "Atendimento por videoconferência via Balcão Virtual do PJe TRF3."
            },
            PrecedenteMarcante = "MS nº 5000001-05.2023.4.03.6100 (Extinção sem resolução do mérito por ausência de prova pré-constituída - Art. 485, IV CPC)."
        };

        _magistrados.TryAdd(preuss.Nome, preuss);
        _magistrados.TryAdd(alex.Nome, alex);
        _magistrados.TryAdd(juizFederal.Nome, juizFederal);
    }

    public Task<IReadOnlyList<PerfilMagistrado>> ListarTodosAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<PerfilMagistrado>>(_magistrados.Values.ToList());
    }

    public Task<PerfilMagistrado?> ObterPorNomeAsync(string nome, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nome)) return Task.FromResult<PerfilMagistrado?>(null);

        // Busca exata ou por termo contido
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
