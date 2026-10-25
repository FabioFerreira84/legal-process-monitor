using System;
using System.Numerics;
using System.Text.RegularExpressions;

namespace LegalProcessMonitor.Domain.ValueObjects;

/// <summary>
/// Value Object que representa o Número Único de Processo Judicial estabelecido pela Resolução CNJ nº 65/2008.
/// Formato: NNNNNNN-DD.AAAA.J.TR.OOOO (20 dígitos)
/// </summary>
public sealed record NumeroProcessoCnj
{
    private static readonly Regex CnjRegex = new(@"^\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}$", RegexOptions.Compiled);

    public string NumeroFormatado { get; }
    public string ApenasDigitos { get; }
    public string Sequencial { get; }
    public string DigitoVerificador { get; }
    public int AnoAjuizamento { get; }
    public int OrgaoJustica { get; }
    public int TribunalId { get; }
    public string ForoOrigem { get; }
    public string SiglaTribunal { get; }

    private NumeroProcessoCnj(
        string formatado,
        string apenasDigitos,
        string sequencial,
        string dv,
        int ano,
        int justica,
        int tribunal,
        string foro,
        string sigla)
    {
        NumeroFormatado = formatado;
        ApenasDigitos = apenasDigitos;
        Sequencial = sequencial;
        DigitoVerificador = dv;
        AnoAjuizamento = ano;
        OrgaoJustica = justica;
        TribunalId = tribunal;
        ForoOrigem = foro;
        SiglaTribunal = sigla;
    }

    public static NumeroProcessoCnj Criar(string numeroProcesso)
    {
        if (string.IsNullOrWhiteSpace(numeroProcesso))
            throw new ArgumentException("O número do processo não pode ser vazio.", nameof(numeroProcesso));

        var clean = Regex.Replace(numeroProcesso, @"[^\d]", "");
        if (clean.Length != 20)
            throw new ArgumentException($"Número de processo deve ter exatamente 20 dígitos numéricos. Recebido: {clean.Length}.", nameof(numeroProcesso));

        var formatado = $"{clean.Substring(0, 7)}-{clean.Substring(7, 2)}.{clean.Substring(9, 4)}.{clean.Substring(13, 1)}.{clean.Substring(14, 2)}.{clean.Substring(16, 4)}";

        if (!ValidarDigitoVerificador(clean))
            throw new ArgumentException($"O dígito verificador do processo '{formatado}' é inválido pelo algoritmo Módulo 97 do CNJ.", nameof(numeroProcesso));

        var sequencial = clean.Substring(0, 7);
        var dv = clean.Substring(7, 2);
        var ano = int.Parse(clean.Substring(9, 4));
        var justica = int.Parse(clean.Substring(13, 1));
        var tribunal = int.Parse(clean.Substring(14, 2));
        var foro = clean.Substring(16, 4);
        var sigla = IdentificarSiglaTribunal(justica, tribunal);

        return new NumeroProcessoCnj(formatado, clean, sequencial, dv, ano, justica, tribunal, foro, sigla);
    }

    public static bool TryParse(string? numeroProcesso, out NumeroProcessoCnj? resultado)
    {
        resultado = null;
        if (string.IsNullOrWhiteSpace(numeroProcesso))
            return false;

        try
        {
            resultado = Criar(numeroProcesso);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string CalcularDigitoVerificador(string sequencial, int ano, int justica, int tribunal, string foro)
    {
        var seqInt = long.Parse(sequencial);
        var r1 = seqInt % 97;
        var r2 = (r1 * 10000 + ano) % 97;
        var r3 = (r2 * 1000 + (justica * 100 + tribunal)) % 97;
        var r4 = (r3 * 10000 + int.Parse(foro)) % 97;
        var r5 = (r4 * 100) % 97;

        var dv = 98 - (int)r5;
        return dv.ToString("D2");
    }

    public static bool ValidarDigitoVerificador(string apenasDigitos)
    {
        if (apenasDigitos.Length != 20) return false;

        var sequencial = apenasDigitos.Substring(0, 7);
        var dv = apenasDigitos.Substring(7, 2);
        var ano = int.Parse(apenasDigitos.Substring(9, 4));
        var justica = int.Parse(apenasDigitos.Substring(13, 1));
        var tribunal = int.Parse(apenasDigitos.Substring(14, 2));
        var foro = apenasDigitos.Substring(16, 4);

        var dvCalculado = CalcularDigitoVerificador(sequencial, ano, justica, tribunal, foro);
        return dv == dvCalculado;
    }

    public static string IdentificarSiglaTribunal(int justica, int tribunal)
    {
        return (justica, tribunal) switch
        {
            (1, _) => "STF",
            (2, _) => "CNJ",
            (3, _) => "STJ",
            (4, 1) => "TRF1",
            (4, 2) => "TRF2",
            (4, 3) => "TRF3",
            (4, 4) => "TRF4",
            (4, 5) => "TRF5",
            (4, 6) => "TRF6",
            (5, 0) => "TST",
            (5, >= 1 and <= 24) => $"TRT{tribunal}",
            (6, 0) => "TSE",
            (7, _) => "STM",
            (8, 1) => "TJAC",
            (8, 2) => "TJAL",
            (8, 3) => "TJAP",
            (8, 4) => "TJAM",
            (8, 5) => "TJBA",
            (8, 6) => "TJCE",
            (8, 7) => "TJDFT",
            (8, 8) => "TJES",
            (8, 9) => "TJGO",
            (8, 10) => "TJMA",
            (8, 11) => "TJMT",
            (8, 12) => "TJMS",
            (8, 13) => "TJMG",
            (8, 14) => "TJPA",
            (8, 15) => "TJPB",
            (8, 16) => "TJPR",
            (8, 17) => "TJPE",
            (8, 18) => "TJPI",
            (8, 19) => "TJRJ",
            (8, 20) => "TJRN",
            (8, 21) => "TJRS",
            (8, 22) => "TJRO",
            (8, 23) => "TJRR",
            (8, 24) => "TJSC",
            (8, 25) => "TJSE",
            (8, 26) => "TJSP",
            (8, 27) => "TJTO",
            _ => $"TRIBUNAL_{justica}_{tribunal:D2}"
        };
    }

    public override string ToString() => NumeroFormatado;
}
