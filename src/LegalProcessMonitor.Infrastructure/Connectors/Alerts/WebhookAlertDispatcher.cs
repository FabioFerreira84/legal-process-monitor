using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace LegalProcessMonitor.Infrastructure.Connectors.Alerts;

public sealed class WebhookAlertDispatcher : IWebhookAlertDispatcher
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookAlertDispatcher> _logger;

    public WebhookAlertDispatcher(HttpClient httpClient, ILogger<WebhookAlertDispatcher> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ResultadoDisparoAlerta> DispararAlertaRadarAsync(
        ConfigRadarOab config,
        IReadOnlyList<ItemRadarDjen> novasIntimacoes,
        CancellationToken ct = default)
    {
        if (novasIntimacoes is null || novasIntimacoes.Count == 0)
        {
            return new ResultadoDisparoAlerta(true, "Nenhum", 200, "Nenhuma intimação nova para disparar.");
        }

        // Prioridade 1: Telegram Bot API
        if (!string.IsNullOrWhiteSpace(config.TelegramBotToken) && !string.IsNullOrWhiteSpace(config.TelegramChatId))
        {
            return await DispararTelegramAsync(config, novasIntimacoes, ct);
        }

        // Prioridade 2: Webhook Corporativo HTTPS (Slack / Teams / Discord / Custom)
        if (!string.IsNullOrWhiteSpace(config.WebhookUrl))
        {
            return await DispararWebhookCorporativoAsync(config, novasIntimacoes, ct);
        }

        return new ResultadoDisparoAlerta(
            Sucesso: true,
            Canal: "Painel Interno",
            StatusCode: 200,
            MensagemDiagnostico: $"{novasIntimacoes.Count} publicação(ões) registrada(s) no Radar interno do escritório (configure Webhook ou Telegram para notificações externas).");
    }

    private async Task<ResultadoDisparoAlerta> DispararTelegramAsync(
        ConfigRadarOab config,
        IReadOnlyList<ItemRadarDjen> itens,
        CancellationToken ct)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"⚖️ *RADAR DJEN — {config.NomeEscritorio}*");
            sb.AppendLine($"Foram localizadas *{itens.Count}* publicação(ões) oficial(is) no Diário de Justiça Eletrônico Nacional:");
            sb.AppendLine();

            foreach (var item in itens.Take(5))
            {
                sb.AppendLine($"• *Processo:* `{item.NumeroProcesso}` ({item.Tribunal})");
                sb.AppendLine($"  *Órgão:* {item.OrgaoJulgador}");
                sb.AppendLine($"  *Data DJEN:* {item.DataDisponibilizacao:dd/MM/yyyy} — {item.TipoComunicacao}");
                sb.AppendLine();
            }

            var url = $"https://api.telegram.org/bot{config.TelegramBotToken!.Trim()}/sendMessage";
            var payload = JsonSerializer.Serialize(new
            {
                chat_id = config.TelegramChatId!.Trim(),
                text = sb.ToString(),
                parse_mode = "Markdown"
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var resp = await _httpClient.PostAsync(url, content, ct);

            return new ResultadoDisparoAlerta(
                Sucesso: resp.IsSuccessStatusCode,
                Canal: "Telegram",
                StatusCode: (int)resp.StatusCode,
                MensagemDiagnostico: resp.IsSuccessStatusCode
                    ? "Resumo diário do DJEN enviado com sucesso para o Telegram."
                    : $"Telegram retornou status {(int)resp.StatusCode}.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao enviar alerta do Radar DJEN para o Telegram do tenant {TenantId}", config.TenantId);
            return new ResultadoDisparoAlerta(false, "Telegram", 500, $"Falha ao contatar API do Telegram: {ex.Message}");
        }
    }

    private async Task<ResultadoDisparoAlerta> DispararWebhookCorporativoAsync(
        ConfigRadarOab config,
        IReadOnlyList<ItemRadarDjen> itens,
        CancellationToken ct)
    {
        if (!EhUrlWebhookSegura(config.WebhookUrl!, out var motivoBloqueio))
        {
            return new ResultadoDisparoAlerta(false, "Webhook", 400, motivoBloqueio);
        }

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                evento = "radar_djen_novas_intimacoes",
                tenantId = config.TenantId,
                escritorio = config.NomeEscritorio,
                totalIntimacoes = itens.Count,
                text = $"[Radar DJEN - {config.NomeEscritorio}] {itens.Count} publicação(ões) oficial(is) capturada(s) no DJEN/CNJ.",
                intimacoes = itens.Take(10).Select(i => new
                {
                    i.Id,
                    i.NumeroProcesso,
                    i.Tribunal,
                    i.OrgaoJulgador,
                    DataDisponibilizacao = i.DataDisponibilizacao.ToString("yyyy-MM-dd"),
                    i.TipoComunicacao,
                    i.TeorResumo,
                    i.LinkInteiroTeor
                })
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var resp = await _httpClient.PostAsync(config.WebhookUrl!.Trim(), content, ct);

            return new ResultadoDisparoAlerta(
                Sucesso: resp.IsSuccessStatusCode,
                Canal: "Webhook",
                StatusCode: (int)resp.StatusCode,
                MensagemDiagnostico: resp.IsSuccessStatusCode
                    ? "Payload JSON disparado com sucesso para o Webhook corporativo."
                    : $"Servidor Webhook respondeu com status {(int)resp.StatusCode}.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao disparar Webhook do tenant {TenantId}", config.TenantId);
            return new ResultadoDisparoAlerta(false, "Webhook", 500, $"Erro de conexão com o Webhook: {ex.Message}");
        }
    }

    public static bool EhUrlWebhookSegura(string urlBruta, out string motivo)
    {
        motivo = string.Empty;
        if (!Uri.TryCreate(urlBruta.Trim(), UriKind.Absolute, out var uri))
        {
            motivo = "URL de Webhook inválida ou bloqueada.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            motivo = "URL de Webhook bloqueada: apenas endpoints HTTPS são permitidos por segurança.";
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host is "localhost" or "127.0.0.1" or "::1" or "0.0.0.0" or "169.254.169.254" ||
            host.EndsWith(".local", StringComparison.Ordinal) ||
            host.EndsWith(".internal", StringComparison.Ordinal))
        {
            motivo = "URL de Webhook bloqueada contra SSRF (endereço local ou interno proibido).";
            return false;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip))
            {
                motivo = "URL de Webhook bloqueada contra SSRF (loopback proibido).";
                return false;
            }

            var bytes = ip.GetAddressBytes();
            if (bytes.Length == 4)
            {
                var privado =
                    bytes[0] == 10 ||
                    bytes[0] == 127 ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168) ||
                    (bytes[0] == 169 && bytes[1] == 254);

                if (privado)
                {
                    motivo = "URL de Webhook bloqueada contra SSRF (faixa IP privada proibida).";
                    return false;
                }
            }
        }

        return true;
    }
}
