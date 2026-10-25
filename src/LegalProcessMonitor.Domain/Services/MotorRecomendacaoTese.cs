using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LegalProcessMonitor.Domain.Entities;

namespace LegalProcessMonitor.Domain.Services;

/// <summary>
/// Motor determinístico e auditável que analisa o que está acontecendo no processo
/// (Classe, Assunto, Últimas Movimentações, Publicações DJEN e Órgão Julgador)
/// e recomenda quais Teses Jurídicas do acervo do escritório devem ser aplicadas,
/// destacando se a tese já foi utilizada anteriormente nos autos ou em casos análogos.
/// </summary>
public static class MotorRecomendacaoTese
{
    public static IReadOnlyList<RecomendacaoTeseResult> RecomendarParaProcesso(
        Processo processo,
        IReadOnlyList<TeseJuridicaTenant> bancoTeses,
        IReadOnlyList<AplicacaoTeseProcesso>? historicoAplicacoes = null)
    {
        ArgumentNullException.ThrowIfNull(processo);
        if (bancoTeses is null || bancoTeses.Count == 0)
            return Array.Empty<RecomendacaoTeseResult>();

        var historico = historicoAplicacoes ?? Array.Empty<AplicacaoTeseProcesso>();
        var cnjLimpo = NormalizarDigitos(processo.NumeroCnjLimpo ?? processo.NumeroCnjFormatado);

        // Constrói o corpus semântico do que está acontecendo no processo agora
        var ultimoAndamento = processo.Movimentacoes.FirstOrDefault();
        var topAndamentos = string.Join(" | ", processo.Movimentacoes.Take(6).Select(m => $"{m.Titulo} {m.Descricao}"));
        var topDocumentos = string.Join(" | ", processo.DocumentosPublicos.Take(4).Select(d => $"{d.Tipo} {d.Titulo} {d.Ementa}"));

        var corpusProcesso = NormalizarTexto(
            $"{processo.Tribunal} {processo.Classe} {processo.Assunto} {processo.OrgaoJulgador} {processo.Status} {topAndamentos} {topDocumentos}");

        var textoRecenteForte = NormalizarTexto(
            $"{ultimoAndamento?.Titulo} {ultimoAndamento?.Descricao} {processo.Classe} {processo.Assunto}");

        var resultados = new List<RecomendacaoTeseResult>();

        foreach (var tese in bancoTeses)
        {
            var termosEncontrados = new List<string>();
            var score = 0;

            foreach (var gatilho in tese.EventosGatilho ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(gatilho)) continue;
                var gNorm = NormalizarTexto(gatilho);
                if (string.IsNullOrWhiteSpace(gNorm)) continue;

                if (textoRecenteForte.Contains(gNorm, StringComparison.Ordinal))
                {
                    score += 32;
                    termosEncontrados.Add(gatilho);
                }
                else if (corpusProcesso.Contains(gNorm, StringComparison.Ordinal))
                {
                    score += 18;
                    termosEncontrados.Add(gatilho);
                }
                else
                {
                    // Match parcial por tokens relevantes (>= 4 letras) do gatilho
                    var tokens = gNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Where(t => t.Length >= 4)
                        .ToList();

                    if (tokens.Count > 0 && tokens.All(tk => corpusProcesso.Contains(tk, StringComparison.Ordinal)))
                    {
                        score += 22;
                        termosEncontrados.Add(gatilho);
                    }
                }
            }

            // Bônus por alinhamento de Área do Direito com Classe/Assunto
            var areaNorm = NormalizarTexto(tese.AreaDireito);
            if (!string.IsNullOrWhiteSpace(areaNorm))
            {
                if (corpusProcesso.Contains("tributar") && areaNorm.Contains("tributar"))
                    score += 18;
                if ((corpusProcesso.Contains("esbulho") || corpusProcesso.Contains("posse") || corpusProcesso.Contains("possessor")) &&
                    (areaNorm.Contains("possessor") || areaNorm.Contains("civil") || areaNorm.Contains("imobiliar")))
                    score += 18;
                if ((corpusProcesso.Contains("aresp") || corpusProcesso.Contains("resp") || corpusProcesso.Contains("stj") || corpusProcesso.Contains("agravo")) &&
                    (areaNorm.Contains("recursal") || areaNorm.Contains("processual") || areaNorm.Contains("tribunais superiores")))
                    score += 16;
                if ((corpusProcesso.Contains("sisbajud") || corpusProcesso.Contains("penhora") || corpusProcesso.Contains("execucao") || corpusProcesso.Contains("bloqueio")) &&
                    areaNorm.Contains("execu"))
                    score += 20;
            }

            // Verifica se a tese já foi aplicada exatamente neste processo
            var jaUsadaNesteProc = historico.Any(h =>
                h.TeseId == tese.Id &&
                NormalizarDigitos(h.NumeroProcesso) == cnjLimpo &&
                !string.IsNullOrWhiteSpace(cnjLimpo));

            if (jaUsadaNesteProc)
            {
                score += 15;
            }
            else if (tese.TotalUsos > 0)
            {
                // Bônus leve de maturidade/validação prática no escritório
                score += Math.Min(8, tese.TotalUsos * 2);
            }

            score = Math.Clamp(score, 10, 99);

            var nivel = score switch
            {
                >= 75 => "Recomendação Crítica (Alta Aderência)",
                >= 45 => "Recomendada para o Ato Atual",
                _ => "Tese Subsidiária / Preventiva"
            };

            var motivo = ConstruirJustificativaTatica(
                processo,
                ultimoAndamento,
                tese,
                termosEncontrados,
                jaUsadaNesteProc);

            resultados.Add(new RecomendacaoTeseResult(
                Tese: tese,
                ScoreAderencia: score,
                NivelAderencia: nivel,
                MotivoEnquadramento: motivo,
                TermosDetectadosNoProcesso: termosEncontrados.Distinct().ToList(),
                JaUtilizadaNesteProcesso: jaUsadaNesteProc));
        }

        return resultados
            .OrderByDescending(r => r.ScoreAderencia)
            .ThenByDescending(r => r.Tese.TotalUsos)
            .ToList();
    }

    private static string ConstruirJustificativaTatica(
        Processo processo,
        Movimentacao? ultimoAndamento,
        TeseJuridicaTenant tese,
        List<string> termosEncontrados,
        bool jaUsadaNesteProc)
    {
        var sb = new StringBuilder();

        if (jaUsadaNesteProc)
        {
            sb.Append("Tese já vinculada anteriormente a estes autos pelo escritório. ");
        }

        if (termosEncontrados.Count > 0)
        {
            sb.Append($"Enquadramento direto detectado nos autos pelos gatilhos [{string.Join(", ", termosEncontrados.Take(4))}] ");
            if (ultimoAndamento != null)
            {
                sb.Append($"diante do andamento '{ultimoAndamento.Titulo}' ({ultimoAndamento.DataHora:dd/MM/yyyy}). ");
            }
        }
        else
        {
            sb.Append($"Compatível como tese de apoio para {processo.Classe ?? "o feito"} ({tese.AreaDireito}). ");
        }

        if (!string.IsNullOrWhiteSpace(tese.PrecedenteChave))
        {
            sb.Append($"Precedente-chave aplicável: {tese.PrecedenteChave}.");
        }

        return sb.ToString().Trim();
    }

    public static string NormalizarTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        var formD = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string NormalizarDigitos(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }
}
