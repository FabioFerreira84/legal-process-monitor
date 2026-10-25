using System;
using System.Globalization;
using System.Text;

namespace LegalProcessMonitor.Domain.Services;

/// <summary>
/// Gerador de eventos de calendário no padrão RFC 5545 (iCalendar / .ics)
/// compatível com Google Calendar, Microsoft Outlook e Apple Calendar.
/// Inclui lembretes proativos em D-2 (48h) e D-1 (24h) antes do vencimento fatal.
/// </summary>
public static class GeradorCalendarioIcs
{
    public static string GerarEventoPrazoFatal(
        string numeroProcesso,
        string classeProcessual,
        string orgaoJulgador,
        CalculoPrazoForenseResult calculo,
        string? responsavel = null)
    {
        var cnj = string.IsNullOrWhiteSpace(numeroProcesso) ? "Processo" : numeroProcesso.Trim();
        var classe = string.IsNullOrWhiteSpace(classeProcessual) ? "Prazo Processual" : classeProcessual.Trim();
        var vara = string.IsNullOrWhiteSpace(orgaoJulgador) ? calculo.SiglaTribunal : orgaoJulgador.Trim();
        var resp = string.IsNullOrWhiteSpace(responsavel) ? "Advogado Responsável" : responsavel.Trim();

        var dtStart = calculo.DataTermoFatal.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var dtEnd = calculo.DataTermoFatal.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var dtStamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var uid = $"prazo-{cnj.Replace(".", "").Replace("-", "")}-{dtStart}@legalprocessmonitor.jus";

        var descBuilder = new StringBuilder();
        descBuilder.Append($"VENCIMENTO FATAL DE PRAZO PROCESSUAL ({calculo.DiasUteisTotal} dias úteis)\\n");
        descBuilder.Append($"Processo CNJ: {EscaparIcs(cnj)}\\n");
        descBuilder.Append($"Classe: {EscaparIcs(classe)}\\n");
        descBuilder.Append($"Órgão Julgador: {EscaparIcs(vara)} ({calculo.SiglaTribunal})\\n");
        descBuilder.Append($"Responsável: {EscaparIcs(resp)}\\n\\n");
        descBuilder.Append($"MEMÓRIA DE CÁLCULO (CPC/2015):\\n");
        descBuilder.Append($"- Disponibilização/Marco: {calculo.DataDisponibilizacao:dd/MM/yyyy}\\n");
        descBuilder.Append($"- Publicação Oficial (Art. 224 §2º): {calculo.DataPublicacao:dd/MM/yyyy}\\n");
        descBuilder.Append($"- Início da Contagem (Art. 224 §3º): {calculo.DataInicioPrazo:dd/MM/yyyy}\\n");
        descBuilder.Append($"- Termo Final Fatal: {calculo.DataTermoFatal:dd/MM/yyyy}\\n");
        descBuilder.Append($"- Base Legal: {EscaparIcs(calculo.BaseLegal)}");

        if (calculo.FeriadosConsiderados.Count > 0)
        {
            descBuilder.Append("\\n\\nFeriados / Suspensões Ignorados:\\n");
            foreach (var f in calculo.FeriadosConsiderados)
            {
                descBuilder.Append($"- {f.Data:dd/MM/yyyy}: {EscaparIcs(f.Descricao)}\\n");
            }
        }

        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\n");
        sb.Append("VERSION:2.0\r\n");
        sb.Append("PRODID:-//LegalProcessMonitor//Calculadora Forense CPC 2015//PT-BR\r\n");
        sb.Append("CALSCALE:GREGORIAN\r\n");
        sb.Append("METHOD:PUBLISH\r\n");
        sb.Append("BEGIN:VEVENT\r\n");
        sb.Append($"UID:{uid}\r\n");
        sb.Append($"DTSTAMP:{dtStamp}\r\n");
        sb.Append($"DTSTART;VALUE=DATE:{dtStart}\r\n");
        sb.Append($"DTEND;VALUE=DATE:{dtEnd}\r\n");
        sb.Append($"SUMMARY:[PRAZO FATAL] {EscaparIcs(cnj)} - {EscaparIcs(classe)} ({calculo.DiasUteisTotal}d úteis)\r\n");
        sb.Append($"LOCATION:{EscaparIcs(vara)} - {calculo.SiglaTribunal}\r\n");
        sb.Append($"DESCRIPTION:{descBuilder}\r\n");
        sb.Append("STATUS:CONFIRMED\r\n");
        sb.Append("TRANSP:OPAQUE\r\n");

        // Alarme 1: D-2 (48 horas antes do prazo fatal)
        sb.Append("BEGIN:VALARM\r\n");
        sb.Append("TRIGGER:-P2D\r\n");
        sb.Append("ACTION:DISPLAY\r\n");
        sb.Append($"DESCRIPTION:Alerta D-2: Prazo fatal do processo {EscaparIcs(cnj)} vence em 2 dias!\r\n");
        sb.Append("END:VALARM\r\n");

        // Alarme 2: D-1 (24 horas antes do prazo fatal)
        sb.Append("BEGIN:VALARM\r\n");
        sb.Append("TRIGGER:-P1D\r\n");
        sb.Append("ACTION:DISPLAY\r\n");
        sb.Append($"DESCRIPTION:URGENTE D-1: Prazo fatal do processo {EscaparIcs(cnj)} vence AMANHÃ!\r\n");
        sb.Append("END:VALARM\r\n");

        sb.Append("END:VEVENT\r\n");
        sb.Append("END:VCALENDAR\r\n");

        return sb.ToString();
    }

    private static string EscaparIcs(string valor)
    {
        return valor
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n");
    }
}
