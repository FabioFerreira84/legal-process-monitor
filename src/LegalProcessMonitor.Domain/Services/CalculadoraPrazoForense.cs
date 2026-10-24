using System;
using System.Collections.Generic;

namespace LegalProcessMonitor.Domain.Services;

public record FeriadoOuSuspensao(DateOnly Data, string Descricao);

public record CalculoPrazoForenseResult
{
    public DateOnly DataDisponibilizacao { get; init; }
    public DateOnly DataPublicacao { get; init; }
    public DateOnly DataInicioPrazo { get; init; }
    public DateOnly DataTermoFatal { get; init; }
    public int DiasPrazoBase { get; init; }
    public bool PrazoEmDobro { get; init; }
    public int DiasUteisTotal { get; init; }
    public int DiasUteisRestantes { get; init; }
    public bool Precluso { get; init; }
    public string SiglaTribunal { get; init; } = "TJSP";
    public string BaseLegal { get; init; } = string.Empty;
    public IReadOnlyList<FeriadoOuSuspensao> FeriadosConsiderados { get; init; } = Array.Empty<FeriadoOuSuspensao>();
}

/// <summary>
/// Motor determinístico de contagem de prazos processuais segundo os arts. 180, 183, 186, 219, 220 e 224 do CPC/2015,
/// Lei nº 14.759/2023 (20 de novembro) e Resolução CNJ nº 455/2022 (sistemática do DJEN).
/// </summary>
public static class CalculadoraPrazoForense
{
    public static CalculoPrazoForenseResult Calcular(
        DateOnly dataMarco,
        int diasPrazoBase,
        bool ehDisponibilizacaoDjen = true,
        bool prazoEmDobro = false,
        string siglaTribunal = "TJSP",
        DateOnly? dataReferenciaHoje = null)
    {
        if (diasPrazoBase <= 0)
            diasPrazoBase = 15;

        var trib = string.IsNullOrWhiteSpace(siglaTribunal) ? "TJSP" : siglaTribunal.Trim().ToUpperInvariant();
        var diasTotal = prazoEmDobro ? diasPrazoBase * 2 : diasPrazoBase;
        var feriadosEncontrados = new List<FeriadoOuSuspensao>();

        DateOnly dataDisponibilizacao = dataMarco;
        DateOnly dataPublicacao;

        if (ehDisponibilizacaoDjen)
        {
            // Art. 224, § 2º CPC e art. 11, § 3º da Res. CNJ 455/2022:
            // Considera-se como data de publicação o primeiro dia útil seguinte ao da disponibilização.
            dataPublicacao = ObterProximoDiaUtil(dataDisponibilizacao, trib, feriadosEncontrados);
        }
        else
        {
            // Quando a data informada já é a publicação ou intimação direta, se cair em dia não útil prorroga-se (Art. 224, § 1º)
            dataPublicacao = EhDiaUtilForense(dataMarco, trib, out _)
                ? dataMarco
                : ObterProximoDiaUtil(dataMarco, trib, feriadosEncontrados);
        }

        // Art. 224, § 3º CPC: A contagem do prazo terá início no primeiro dia útil que seguir ao da publicação.
        var dataInicio = ObterProximoDiaUtil(dataPublicacao, trib, feriadosEncontrados);

        // O dia do início já é o 1º dia útil do prazo; somamos (diasTotal - 1) dias úteis subsequentes.
        var dataTermoFatal = dataInicio;
        var contados = 1;

        while (contados < diasTotal)
        {
            dataTermoFatal = dataTermoFatal.AddDays(1);
            if (EhDiaUtilForense(dataTermoFatal, trib, out var motivo))
            {
                contados++;
            }
            else if ( EhDiaDeSemana(dataTermoFatal) && !string.IsNullOrEmpty(motivo))
            {
                RegistrarFeriadoUnico(feriadosEncontrados, dataTermoFatal, motivo);
            }
        }

        var hoje = dataReferenciaHoje ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        var diasRestantes = CalcularDiasUteisRestantes(hoje, dataTermoFatal, trib);

        var baseLegal = ConstruirBaseLegal(diasPrazoBase, ehDisponibilizacaoDjen, prazoEmDobro);

        return new CalculoPrazoForenseResult
        {
            DataDisponibilizacao = dataDisponibilizacao,
            DataPublicacao = dataPublicacao,
            DataInicioPrazo = dataInicio,
            DataTermoFatal = dataTermoFatal,
            DiasPrazoBase = diasPrazoBase,
            PrazoEmDobro = prazoEmDobro,
            DiasUteisTotal = diasTotal,
            DiasUteisRestantes = diasRestantes,
            Precluso = hoje > dataTermoFatal,
            SiglaTribunal = trib,
            BaseLegal = baseLegal,
            FeriadosConsiderados = feriadosEncontrados
        };
    }

    public static int CalcularDiasUteisRestantes(DateOnly dataReferencia, DateOnly dataTermoFatal, string siglaTribunal = "TJSP")
    {
        if (dataReferencia >= dataTermoFatal)
            return 0;

        var d = dataReferencia;
        var cont = 0;
        while (d < dataTermoFatal)
        {
            d = d.AddDays(1);
            if (EhDiaUtilForense(d, siglaTribunal, out _))
            {
                cont++;
            }
        }
        return cont;
    }

    public static bool EhDiaUtilForense(DateOnly data, string siglaTribunal, out string? motivoNaoUtil)
    {
        motivoNaoUtil = null;

        if (data.DayOfWeek == DayOfWeek.Saturday)
        {
            motivoNaoUtil = "Sábado (Art. 219 CPC)";
            return false;
        }
        if (data.DayOfWeek == DayOfWeek.Sunday)
        {
            motivoNaoUtil = "Domingo (Art. 219 CPC)";
            return false;
        }

        // 1. Recesso Forense e Suspensão de Prazos (Art. 220 CPC/2015: 20 de dezembro a 20 de janeiro)
        if ((data.Month == 12 && data.Day >= 20) || (data.Month == 1 && data.Day <= 20))
        {
            motivoNaoUtil = "Suspensão de Prazos Processuais / Recesso Forense (Art. 220 CPC - 20/dez a 20/jan)";
            return false;
        }

        // 2. Feriados Nacionais Fixos + Feriados Forenses Nacionais
        var mesDia = (data.Month, data.Day);
        switch (mesDia)
        {
            case (1, 1):
                motivoNaoUtil = "Confraternização Universal (Feriado Nacional)";
                return false;
            case (4, 21):
                motivoNaoUtil = "Tiradentes (Feriado Nacional)";
                return false;
            case (5, 1):
                motivoNaoUtil = "Dia do Trabalhador (Feriado Nacional)";
                return false;
            case (8, 11):
                motivoNaoUtil = "Dia da Instituição dos Cursos Jurídicos / Dia do Advogado (Feriado Forense - Lei 5.010/66)";
                return false;
            case (9, 7):
                motivoNaoUtil = "Independência do Brasil (Feriado Nacional)";
                return false;
            case (10, 12):
                motivoNaoUtil = "Nossa Senhora Aparecida (Feriado Nacional)";
                return false;
            case (10, 28):
                motivoNaoUtil = "Dia do Servidor Público (Expediente Forense Suspenso - Art. 236 Lei 8.112/90)";
                return false;
            case (11, 2):
                motivoNaoUtil = "Finados (Feriado Nacional)";
                return false;
            case (11, 15):
                motivoNaoUtil = "Proclamação da República (Feriado Nacional)";
                return false;
            case (11, 20):
                motivoNaoUtil = "Dia Nacional de Zumbi e da Consciência Negra (Feriado Nacional - Lei 14.759/2023)";
                return false;
            case (12, 8):
                motivoNaoUtil = "Dia da Justiça (Feriado Forense - Decreto-Lei 8.292/45 e Lei 5.010/66)";
                return false;
            case (12, 25):
                motivoNaoUtil = "Natal (Feriado Nacional)";
                return false;
        }

        // 3. Feriados Estaduais / Locais de São Paulo (TJSP, TRF3, TRT2, TRT15)
        var trib = (siglaTribunal ?? "TJSP").ToUpperInvariant();
        if (trib is "TJSP" or "TRF3" or "TRT2" or "TRT15")
        {
            if (mesDia == (1, 25))
            {
                motivoNaoUtil = "Aniversário da Cidade de São Paulo (Feriado Municipal SP)";
                return false;
            }
            if (mesDia == (7, 9))
            {
                motivoNaoUtil = "Revolução Constitucionalista de 1932 (Feriado Estadual SP - Lei 9.497/97)";
                return false;
            }
        }

        // 4. Feriados Móveis baseados no Domingo de Páscoa (Algoritmo de Meeus/Jones/Butcher)
        var pascoa = CalcularDomingoDePascoa(data.Year);
        var segundaCarnaval = pascoa.AddDays(-48);
        var tercaCarnaval = pascoa.AddDays(-47);
        var quintaSanta = pascoa.AddDays(-3);
        var sextaSanta = pascoa.AddDays(-2);
        var corpusChristi = pascoa.AddDays(60);

        if (data == segundaCarnaval || data == tercaCarnaval)
        {
            motivoNaoUtil = "Carnaval (Expediente Forense Suspenso)";
            return false;
        }
        if (data == quintaSanta)
        {
            motivoNaoUtil = "Quinta-feira Santa / Endoenças (Feriado Forense - Lei 5.010/66, art. 62, II)";
            return false;
        }
        if (data == sextaSanta)
        {
            motivoNaoUtil = "Sexta-feira Santa / Paixão de Cristo (Feriado Nacional / Forense)";
            return false;
        }
        if (data == corpusChristi)
        {
            motivoNaoUtil = "Corpus Christi (Feriado Forense)";
            return false;
        }

        return true;
    }

    public static DateOnly CalcularDomingoDePascoa(int ano)
    {
        int a = ano % 19;
        int b = ano / 100;
        int c = ano % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int mes = (h + l - 7 * m + 114) / 31;
        int dia = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(ano, mes, dia);
    }

    private static DateOnly ObterProximoDiaUtil(DateOnly dataBase, string siglaTribunal, List<FeriadoOuSuspensao> feriados)
    {
        var cursor = dataBase.AddDays(1);
        while (!EhDiaUtilForense(cursor, siglaTribunal, out var motivo))
        {
            if (EhDiaDeSemana(cursor) && !string.IsNullOrEmpty(motivo))
            {
                RegistrarFeriadoUnico(feriados, cursor, motivo);
            }
            cursor = cursor.AddDays(1);
        }
        return cursor;
    }

    private static bool EhDiaDeSemana(DateOnly d) =>
        d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday;

    private static void RegistrarFeriadoUnico(List<FeriadoOuSuspensao> lista, DateOnly data, string motivo)
    {
        if (!lista.Exists(f => f.Data == data))
        {
            lista.Add(new FeriadoOuSuspensao(data, motivo));
        }
    }

    private static string ConstruirBaseLegal(int diasPrazoBase, bool ehDisponibilizacaoDjen, bool prazoEmDobro)
    {
        var rito = diasPrazoBase == 5
            ? "Art. 1.023 CPC (Embargos de Declaração - 5 dias úteis)"
            : $"Art. 1.003, § 5º c/c Art. 219 CPC ({diasPrazoBase} dias úteis)";

        var djen = ehDisponibilizacaoDjen
            ? " + Art. 224, §§ 2º e 3º CPC / Res. CNJ 455/2022 (DJEN)"
            : " + Art. 224, § 3º CPC";

        var dobro = prazoEmDobro
            ? " + Prazo em Dobro (Art. 183, 180 e 186 do CPC)"
            : string.Empty;

        return rito + djen + dobro;
    }
}
