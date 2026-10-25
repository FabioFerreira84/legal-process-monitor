using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;
using LegalProcessMonitor.Domain.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LegalProcessMonitor.Api.Controllers;

[ApiController]
[Route("api/workspaces")]
public sealed class WorkspacesController : ControllerBase
{
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IEsajOabScraper _oabScraper;
    private readonly IWebhookAlertDispatcher _alertDispatcher;
    private readonly IProcessoRepository? _processoRepository;
    private readonly IInteligenciaPublicaConnector? _inteligenciaConnector;

    public WorkspacesController(
        IWorkspaceRepository workspaceRepository,
        IEsajOabScraper oabScraper,
        IWebhookAlertDispatcher alertDispatcher,
        IProcessoRepository? processoRepository = null,
        IInteligenciaPublicaConnector? inteligenciaConnector = null)
    {
        _workspaceRepository = workspaceRepository;
        _oabScraper = oabScraper;
        _alertDispatcher = alertDispatcher;
        _processoRepository = processoRepository;
        _inteligenciaConnector = inteligenciaConnector;
    }

    private string ResolverTenantId(string? queryTenantId = null)
    {
        var headerTenant = Request.Headers["X-Tenant-Id"].FirstOrDefault();
        var candidato = !string.IsNullOrWhiteSpace(headerTenant)
            ? headerTenant
            : !string.IsNullOrWhiteSpace(queryTenantId)
                ? queryTenantId
                : "escritorio-ferreira";

        return TenantValidador.NormalizarEValidar(candidato);
    }

    [HttpGet]
    [ProducesResponseType(typeof(EstadoWorkspaceTenant), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ObterEstado([FromQuery] string? tenantId = null, CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId(tenantId);
            var estado = await _workspaceRepository.ObterEstadoWorkspaceAsync(tenant, ct);
            return Ok(estado);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    public sealed record AtualizarConfigRadarRequest(
        string NomeEscritorio,
        List<string> InscricoesOab,
        string? WebhookUrl,
        string? TelegramBotToken,
        string? TelegramChatId,
        bool Ativo = true);

    [HttpPut("config")]
    public async Task<IActionResult> AtualizarConfig([FromBody] AtualizarConfigRadarRequest req, CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            var oabs = (req.InscricoesOab ?? new List<string>())
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => o.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();

            var config = new ConfigRadarOab(
                TenantId: tenant,
                NomeEscritorio: string.IsNullOrWhiteSpace(req.NomeEscritorio) ? $"Workspace {tenant}" : req.NomeEscritorio.Trim(),
                InscricoesOab: oabs,
                WebhookUrl: string.IsNullOrWhiteSpace(req.WebhookUrl) ? null : req.WebhookUrl.Trim(),
                TelegramBotToken: string.IsNullOrWhiteSpace(req.TelegramBotToken) ? null : req.TelegramBotToken.Trim(),
                TelegramChatId: string.IsNullOrWhiteSpace(req.TelegramChatId) ? null : req.TelegramChatId.Trim(),
                Ativo: req.Ativo,
                UltimaSincronizacaoUtc: DateTime.UtcNow);

            var salvo = await _workspaceRepository.SalvarConfigRadarAsync(config, ct);
            return Ok(salvo);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    public sealed record SalvarWatchlistRequest(
        string NumeroProcesso,
        string Classe,
        string OrgaoJulgador,
        string Tribunal,
        int TotalMovimentacoesConhecidas,
        bool TemNovidade = false);

    [HttpPost("watchlist")]
    public async Task<IActionResult> SalvarWatchlist([FromBody] SalvarWatchlistRequest req, CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            var item = new ItemWatchlistTenant(
                TenantId: tenant,
                NumeroProcesso: req.NumeroProcesso,
                Classe: req.Classe,
                OrgaoJulgador: req.OrgaoJulgador,
                Tribunal: req.Tribunal,
                TotalMovimentacoesConhecidas: req.TotalMovimentacoesConhecidas,
                AdicionadoEmUtc: DateTime.UtcNow,
                TemNovidade: req.TemNovidade);

            var salvo = await _workspaceRepository.SalvarItemWatchlistAsync(item, ct);
            return Ok(salvo);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    [HttpDelete("watchlist/{numeroProcesso}")]
    public async Task<IActionResult> RemoverWatchlist(string numeroProcesso, CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            var removido = await _workspaceRepository.RemoverItemWatchlistAsync(tenant, Uri.UnescapeDataString(numeroProcesso), ct);
            return Ok(new { removido });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    public sealed record SalvarNotaJuizoRequest(string ChaveUnidade, string ConteudoNota);

    [HttpPut("notas-juizo")]
    public async Task<IActionResult> SalvarNotaJuizo([FromBody] SalvarNotaJuizoRequest req, CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            var salvo = await _workspaceRepository.SalvarNotaJuizoAsync(new NotaJuizoTenant(
                TenantId: tenant,
                ChaveUnidade: req.ChaveUnidade,
                ConteudoNota: req.ConteudoNota,
                AtualizadoEmUtc: DateTime.UtcNow), ct);

            return Ok(salvo);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    public sealed record CriarSuspensaoLocalRequest(string DataIso, string Descricao, string ComarcaOuTribunal = "TJSP");

    [HttpPost("suspensoes")]
    public async Task<IActionResult> AdicionarSuspensaoLocal([FromBody] CriarSuspensaoLocalRequest req, CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            if (!DateOnly.TryParse(req.DataIso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
            {
                return BadRequest(new { mensagem = "Data de suspensão inválida. Use o formato YYYY-MM-DD." });
            }

            if (string.IsNullOrWhiteSpace(req.Descricao))
            {
                return BadRequest(new { mensagem = "Informe a descrição/ato normativo da suspensão (ex: Provimento CSM ou Feriado Municipal)." });
            }

            var suspensao = new SuspensaoForenseLocal(
                Id: Guid.NewGuid().ToString("N"),
                TenantId: tenant,
                Data: data,
                Descricao: req.Descricao.Trim(),
                ComarcaOuTribunal: string.IsNullOrWhiteSpace(req.ComarcaOuTribunal) ? "TJSP" : req.ComarcaOuTribunal.Trim().ToUpperInvariant(),
                CriadoEmUtc: DateTime.UtcNow);

            var salva = await _workspaceRepository.AdicionarSuspensaoLocalAsync(suspensao, ct);
            return Ok(salva);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    [HttpDelete("suspensoes/{id}")]
    public async Task<IActionResult> RemoverSuspensaoLocal(string id, CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            var removido = await _workspaceRepository.RemoverSuspensaoLocalAsync(tenant, id, ct);
            return Ok(new { removido });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    public sealed record SalvarTarefaKanbanRequest(
        string? Id,
        string NumeroProcesso,
        string Titulo,
        string? Descricao,
        string? Responsavel,
        StatusTarefaColuna Coluna,
        PrioridadeTarefa Prioridade,
        string PrazoFatalIso,
        int DiasPrazo = 15,
        bool PrazoEmDobro = false,
        string? NumeroProtocolo = null);

    [HttpPost("tarefas")]
    public async Task<IActionResult> SalvarTarefa([FromBody] SalvarTarefaKanbanRequest req, CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            if (string.IsNullOrWhiteSpace(req.Titulo))
            {
                return BadRequest(new { mensagem = "O título da tarefa processual é obrigatório." });
            }

            if (!DateOnly.TryParse(req.PrazoFatalIso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var prazoFatal))
            {
                prazoFatal = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15));
            }

            var tarefa = new TarefaProcessual(
                Id: string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("N") : req.Id.Trim(),
                TenantId: tenant,
                NumeroProcesso: string.IsNullOrWhiteSpace(req.NumeroProcesso) ? "N/A" : req.NumeroProcesso.Trim(),
                Titulo: req.Titulo.Trim(),
                Descricao: req.Descricao?.Trim() ?? string.Empty,
                Responsavel: string.IsNullOrWhiteSpace(req.Responsavel) ? "Advogado Responsável" : req.Responsavel.Trim(),
                Coluna: req.Coluna,
                Prioridade: req.Prioridade,
                PrazoFatal: prazoFatal,
                DiasPrazo: req.DiasPrazo <= 0 ? 15 : req.DiasPrazo,
                PrazoEmDobro: req.PrazoEmDobro,
                NumeroProtocolo: string.IsNullOrWhiteSpace(req.NumeroProtocolo) ? null : req.NumeroProtocolo.Trim(),
                CriadoEmUtc: DateTime.UtcNow,
                AtualizadoEmUtc: DateTime.UtcNow);

            var salva = await _workspaceRepository.SalvarTarefaAsync(tarefa, ct);
            return Ok(salva);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    [HttpDelete("tarefas/{id}")]
    public async Task<IActionResult> RemoverTarefa(string id, CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            var removido = await _workspaceRepository.RemoverTarefaAsync(tenant, id, ct);
            return Ok(new { removido });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    public sealed record SincronizarRadarRequest(string? OabAdicional = null, bool DispararAlerta = true);

    [HttpPost("radar/sincronizar")]
    public async Task<IActionResult> SincronizarRadarDjen([FromBody] SincronizarRadarRequest? req, CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            var estadoAtual = await _workspaceRepository.ObterEstadoWorkspaceAsync(tenant, ct);
            var oabs = estadoAtual.Configuracao.InscricoesOab.ToList();

            if (!string.IsNullOrWhiteSpace(req?.OabAdicional))
            {
                var oabNova = req.OabAdicional.Trim().ToUpperInvariant();
                if (!oabs.Contains(oabNova))
                {
                    oabs.Add(oabNova);
                }
            }

            if (oabs.Count == 0)
            {
                oabs.Add("238470/SP");
            }

            var novosItens = new List<ItemRadarDjen>();
            foreach (var oab in oabs.Take(3))
            {
                var carteira = await _oabScraper.ConsultarProcessosPorOabAsync(oab, ct);
                foreach (var proc in carteira.Processos.Take(25))
                {
                    var dataDisp = DateTime.UtcNow;
                    if (!string.IsNullOrWhiteSpace(proc.DataRecebimento) &&
                        DateTime.TryParseExact(proc.DataRecebimento, new[] { "dd/MM/yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dtParsed))
                    {
                        dataDisp = dtParsed;
                    }

                    var idDeterministico = $"djen-{Regex.Replace(proc.NumeroCnjFormatado ?? string.Empty, @"\D", "")}-{dataDisp:yyyyMMdd}";
                    novosItens.Add(new ItemRadarDjen(
                        Id: idDeterministico,
                        TenantId: tenant,
                        OabOrigem: oab,
                        NumeroProcesso: proc.NumeroCnjFormatado ?? proc.NumeroCnjLimpo,
                        Tribunal: proc.Tribunal ?? "TJSP",
                        OrgaoJulgador: proc.Vara ?? proc.Foro ?? "Unidade Judiciária",
                        DataDisponibilizacao: dataDisp,
                        TipoComunicacao: proc.Classe ?? "Comunicação Processual DJEN",
                        TeorResumo: proc.UltimaComunicacaoResumo ?? $"Publicação oficial localizada para {proc.NumeroCnjFormatado} ({proc.Papel}).",
                        LinkInteiroTeor: proc.UrlOrigem ?? "https://comunica.pje.jus.br",
                        Lida: false,
                        ConvertidaEmTarefa: false,
                        CapturadoEmUtc: DateTime.UtcNow));
                }
            }

            var inseridos = await _workspaceRepository.SalvarItensRadarLoteAsync(tenant, novosItens, ct);

            var configAtualizada = estadoAtual.Configuracao with
            {
                InscricoesOab = oabs,
                UltimaSincronizacaoUtc = DateTime.UtcNow
            };
            await _workspaceRepository.SalvarConfigRadarAsync(configAtualizada, ct);

            var todosRadar = await _workspaceRepository.ListarItensRadarAsync(tenant, ct);

            ResultadoDisparoAlerta? alertaResultado = null;
            if (req?.DispararAlerta != false)
            {
                alertaResultado = await _alertDispatcher.DispararAlertaRadarAsync(
                    configAtualizada,
                    todosRadar.Take(5).ToList(),
                    ct);
            }

            return Ok(new
            {
                novasPublicacoesInseridas = inseridos,
                totalNoRadar = todosRadar.Count,
                configuracao = configAtualizada,
                itensRadar = todosRadar,
                alerta = alertaResultado
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    public sealed record ConverterRadarEmTarefaRequest(
        int DiasPrazoBase = 15,
        bool PrazoEmDobro = false,
        string? Responsavel = null);

    [HttpPost("radar/{id}/converter-tarefa")]
    public async Task<IActionResult> ConverterRadarEmTarefa(
        string id,
        [FromBody] ConverterRadarEmTarefaRequest? req,
        CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            var itens = await _workspaceRepository.ListarItensRadarAsync(tenant, ct);
            var item = itens.FirstOrDefault(i => i.Id == id);
            if (item is null)
            {
                return NotFound(new { mensagem = "Publicação do Radar DJEN não encontrada neste workspace." });
            }

            var suspensoes = await _workspaceRepository.ListarSuspensoesLocaisAsync(tenant, ct);
            var feriadosLocais = suspensoes.Select(s => new FeriadoOuSuspensao(s.Data, s.Descricao)).ToList();

            var diasBase = req?.DiasPrazoBase is > 0 ? req.DiasPrazoBase : 15;
            var dobro = req?.PrazoEmDobro ?? false;

            var calculo = CalculadoraPrazoForense.Calcular(
                dataMarco: DateOnly.FromDateTime(item.DataDisponibilizacao),
                diasPrazoBase: diasBase,
                ehDisponibilizacaoDjen: true,
                prazoEmDobro: dobro,
                siglaTribunal: item.Tribunal,
                suspensoesLocais: feriadosLocais);

            var tarefa = new TarefaProcessual(
                Id: $"radar-task-{item.Id}",
                TenantId: tenant,
                NumeroProcesso: item.NumeroProcesso,
                Titulo: $"Cumprir Intimação DJEN ({item.TipoComunicacao})",
                Descricao: item.TeorResumo,
                Responsavel: string.IsNullOrWhiteSpace(req?.Responsavel) ? $"OAB {item.OabOrigem}" : req!.Responsavel.Trim(),
                Coluna: StatusTarefaColuna.Intimacao,
                Prioridade: PrioridadeTarefa.Critica,
                PrazoFatal: calculo.DataTermoFatal,
                DiasPrazo: calculo.DiasUteisTotal,
                PrazoEmDobro: dobro,
                NumeroProtocolo: null,
                CriadoEmUtc: DateTime.UtcNow,
                AtualizadoEmUtc: DateTime.UtcNow);

            var tarefaSalva = await _workspaceRepository.SalvarTarefaAsync(tarefa, ct);
            await _workspaceRepository.MarcarItemRadarLidoAsync(tenant, item.Id, convertidaEmTarefa: true, ct);

            return Ok(new
            {
                tarefa = tarefaSalva,
                calculoPrazo = calculo
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    // =========================================================================
    // BANCO DE TESES JURÍDICAS MULTI-TENANT & MOTOR DE RECOMENDAÇÃO TÁTICA
    // =========================================================================
    public sealed record SalvarTeseJuridicaRequest(
        string? Id,
        string Titulo,
        string AreaDireito,
        List<string>? EventosGatilho,
        string FundamentacaoLegal,
        string PrecedenteChave,
        string ModeloArgumentacao,
        int TotalUsos = 0,
        string? UltimoProcessoAplicado = null);

    [HttpGet("teses")]
    public async Task<IActionResult> ListarTeses(CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            var teses = await _workspaceRepository.ListarTesesJuridicasAsync(tenant, ct);
            var aplicacoes = await _workspaceRepository.ListarAplicacoesTesesAsync(tenant, null, ct);
            return Ok(new { teses, aplicacoes });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    [HttpPost("teses")]
    public async Task<IActionResult> SalvarTese([FromBody] SalvarTeseJuridicaRequest req, CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            if (string.IsNullOrWhiteSpace(req.Titulo))
            {
                return BadRequest(new { mensagem = "O título da tese jurídica é obrigatório." });
            }

            var gatilhos = (req.EventosGatilho ?? new List<string>())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                .Distinct()
                .ToList();

            var tese = new TeseJuridicaTenant(
                Id: string.IsNullOrWhiteSpace(req.Id) ? $"tese-{Guid.NewGuid():N}" : req.Id.Trim(),
                TenantId: tenant,
                Titulo: req.Titulo.Trim(),
                AreaDireito: string.IsNullOrWhiteSpace(req.AreaDireito) ? "Contencioso Cível" : req.AreaDireito.Trim(),
                EventosGatilho: gatilhos,
                FundamentacaoLegal: req.FundamentacaoLegal?.Trim() ?? string.Empty,
                PrecedenteChave: req.PrecedenteChave?.Trim() ?? string.Empty,
                ModeloArgumentacao: req.ModeloArgumentacao?.Trim() ?? string.Empty,
                TotalUsos: Math.Max(0, req.TotalUsos),
                UltimoProcessoAplicado: req.UltimoProcessoAplicado,
                AtualizadoEmUtc: DateTime.UtcNow);

            var salva = await _workspaceRepository.SalvarTeseJuridicaAsync(tese, ct);
            return Ok(salva);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    [HttpDelete("teses/{id}")]
    public async Task<IActionResult> RemoverTese(string id, CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            var removido = await _workspaceRepository.RemoverTeseJuridicaAsync(tenant, id, ct);
            return Ok(new { removido });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    public sealed record AplicarTeseProcessoRequest(
        string TeseId,
        string NumeroProcesso,
        string? EventoContexto = null,
        string? ResultadoEsperadoOuObtido = null,
        string? Responsavel = null,
        bool CriarTarefaKanban = false,
        string? PrazoFatalIso = null);

    [HttpPost("teses/aplicar")]
    public async Task<IActionResult> AplicarTeseNoProcesso([FromBody] AplicarTeseProcessoRequest req, CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            if (string.IsNullOrWhiteSpace(req.TeseId) || string.IsNullOrWhiteSpace(req.NumeroProcesso))
            {
                return BadRequest(new { mensagem = "Informe a tese (TeseId) e o número do processo (NumeroProcesso)." });
            }

            var teses = await _workspaceRepository.ListarTesesJuridicasAsync(tenant, ct);
            var tese = teses.FirstOrDefault(t => t.Id == req.TeseId);
            var tituloTese = tese?.Titulo ?? req.TeseId;

            var aplicacao = new AplicacaoTeseProcesso(
                Id: $"aplic-{Guid.NewGuid():N}",
                TenantId: tenant,
                TeseId: req.TeseId.Trim(),
                TituloTese: tituloTese,
                NumeroProcesso: req.NumeroProcesso.Trim(),
                EventoContexto: string.IsNullOrWhiteSpace(req.EventoContexto) ? "Aplicação tática em manifestação/recurso" : req.EventoContexto.Trim(),
                ResultadoEsperadoOuObtido: string.IsNullOrWhiteSpace(req.ResultadoEsperadoOuObtido) ? "Tese vinculada à minuta em elaboração" : req.ResultadoEsperadoOuObtido.Trim(),
                Responsavel: string.IsNullOrWhiteSpace(req.Responsavel) ? "Advogado Responsável" : req.Responsavel.Trim(),
                AplicadaEmUtc: DateTime.UtcNow);

            var registrada = await _workspaceRepository.RegistrarAplicacaoTeseAsync(aplicacao, ct);

            TarefaProcessual? tarefaCriada = null;
            if (req.CriarTarefaKanban)
            {
                if (!DateOnly.TryParse(req.PrazoFatalIso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var prazoFatal))
                {
                    prazoFatal = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15));
                }

                var novaTarefa = new TarefaProcessual(
                    Id: $"tese-task-{Guid.NewGuid():N}",
                    TenantId: tenant,
                    NumeroProcesso: req.NumeroProcesso.Trim(),
                    Titulo: $"Peticionar com Tese: {tituloTese}",
                    Descricao: tese != null
                        ? $"Fundamentação: {tese.FundamentacaoLegal}\nPrecedente: {tese.PrecedenteChave}\n\n{tese.ModeloArgumentacao}"
                        : aplicacao.EventoContexto,
                    Responsavel: aplicacao.Responsavel,
                    Coluna: StatusTarefaColuna.Elaboracao,
                    Prioridade: PrioridadeTarefa.Critica,
                    PrazoFatal: prazoFatal,
                    DiasPrazo: 15,
                    PrazoEmDobro: false,
                    NumeroProtocolo: null,
                    CriadoEmUtc: DateTime.UtcNow,
                    AtualizadoEmUtc: DateTime.UtcNow);

                tarefaCriada = await _workspaceRepository.SalvarTarefaAsync(novaTarefa, ct);
            }

            var tesesAtualizadas = await _workspaceRepository.ListarTesesJuridicasAsync(tenant, ct);
            return Ok(new
            {
                aplicacao = registrada,
                tarefaKanban = tarefaCriada,
                teses = tesesAtualizadas
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    [HttpGet("teses/recomendar/{numeroProcesso}")]
    public async Task<IActionResult> RecomendarTesesParaProcesso(string numeroProcesso, CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            var digitos = Regex.Replace(Uri.UnescapeDataString(numeroProcesso ?? ""), @"\D", "");
            var teses = await _workspaceRepository.ListarTesesJuridicasAsync(tenant, ct);
            var historico = await _workspaceRepository.ListarAplicacoesTesesAsync(tenant, null, ct);

            Processo? processo = null;
            if (_processoRepository != null && !string.IsNullOrWhiteSpace(digitos))
            {
                processo = await _processoRepository.ObterPorNumeroAsync(digitos, ct);
            }

            if (processo is null)
            {
                return NotFound(new { mensagem = "Processo não encontrado no cache para análise de recomendação." });
            }

            var recomendacoes = MotorRecomendacaoTese.RecomendarParaProcesso(processo, teses, historico);
            return Ok(recomendacoes);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    // =========================================================================
    // FICHA PATRIMONIAL / QSA DA PARTE ADVERSA + FERIADOS AUTOMÁTICOS
    // =========================================================================
    [HttpGet("partes/ficha-patrimonial")]
    public async Task<IActionResult> ConsultarFichaPatrimonial(
        [FromQuery] string termo,
        [FromQuery] string? comarca = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(termo))
        {
            return BadRequest(new { mensagem = "Informe a razão social ou CNPJ da parte processual." });
        }

        if (_inteligenciaConnector is null)
        {
            return StatusCode(503, new { mensagem = "Conector de inteligência pública não configurado." });
        }

        var ficha = await _inteligenciaConnector.ConsultarFichaPatrimonialParteAsync(termo, comarca, ct);
        return Ok(ficha);
    }

    public sealed record SincronizarFeriadosRequest(int Ano = 2026, string SiglaTribunal = "TJSP");

    [HttpPost("suspensoes/sincronizar-oficiais")]
    public async Task<IActionResult> SincronizarFeriadosOficiais(
        [FromBody] SincronizarFeriadosRequest? req,
        CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            if (_inteligenciaConnector is null)
            {
                return StatusCode(503, new { mensagem = "Conector de inteligência pública não configurado." });
            }

            var ano = req?.Ano is >= 2024 and <= 2030 ? req.Ano : DateTime.UtcNow.Year;
            var sigla = string.IsNullOrWhiteSpace(req?.SiglaTribunal) ? "TJSP" : req!.SiglaTribunal.Trim().ToUpperInvariant();

            var feriados = await _inteligenciaConnector.ObterFeriadosOficiaisAnoAsync(tenant, ano, sigla, ct);
            foreach (var f in feriados)
            {
                await _workspaceRepository.AdicionarSuspensaoLocalAsync(f, ct);
            }

            var todas = await _workspaceRepository.ListarSuspensoesLocaisAsync(tenant, ct);
            return Ok(new
            {
                sincronizados = feriados.Count,
                suspensoesLocais = todas
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    // =========================================================================
    // FATIA 7: BACKUP & RESTORE ATÔMICO MULTI-TENANT (CLOUD PERSISTENCE)
    // =========================================================================
    [HttpGet("backup")]
    public async Task<IActionResult> ExportarBackupWorkspace(CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            var estado = await _workspaceRepository.ObterEstadoWorkspaceAsync(tenant, ct);
            return Ok(estado);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    public sealed record RestaurarBackupRequest(
        EstadoWorkspaceTenant Snapshot,
        bool MesclarComExistente = true);

    [HttpPost("restore")]
    public async Task<IActionResult> RestaurarBackupWorkspace(
        [FromBody] RestaurarBackupRequest req,
        CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            if (req?.Snapshot is null)
            {
                return BadRequest(new { mensagem = "Payload de snapshot inválido para restauração." });
            }

            var estadoRestaurado = await _workspaceRepository.RestaurarSnapshotWorkspaceAsync(
                tenant,
                req.Snapshot,
                req.MesclarComExistente,
                ct);

            return Ok(estadoRestaurado);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    // =========================================================================
    // FATIA 8: GERADOR AUTOMÁTICO DE MINUTA / PEÇA PROCESSUAL (.DOC WORD)
    // =========================================================================
    public sealed record GerarMinutaProcessualRequest(
        string TeseId,
        string NumeroProcesso,
        string? ClasseProcessual = null,
        string? AssuntoPrincipal = null,
        string? OrgaoJulgador = null,
        string? PoloAtivo = null,
        string? PoloPassivo = null,
        string? UltimoAndamento = null,
        string? DataDisponibilizacaoIso = null,
        int DiasPrazo = 15);

    [HttpPost("teses/gerar-minuta")]
    public async Task<IActionResult> GerarMinutaProcessual(
        [FromBody] GerarMinutaProcessualRequest req,
        CancellationToken ct = default)
    {
        try
        {
            var tenant = ResolverTenantId();
            if (string.IsNullOrWhiteSpace(req.TeseId) || string.IsNullOrWhiteSpace(req.NumeroProcesso))
            {
                return BadRequest(new { mensagem = "Informe a tese jurídica (TeseId) e o número do processo (NumeroProcesso)." });
            }

            var estado = await _workspaceRepository.ObterEstadoWorkspaceAsync(tenant, ct);
            var teses = estado.TesesJuridicas ?? await _workspaceRepository.ListarTesesJuridicasAsync(tenant, ct);
            var tese = teses.FirstOrDefault(t => t.Id == req.TeseId) ?? teses.FirstOrDefault();

            if (tese is null)
            {
                return NotFound(new { mensagem = "Tese jurídica não encontrada neste workspace." });
            }

            // Enriquecer com dados reais do processo no repositório caso disponíveis
            var digitos = Regex.Replace(req.NumeroProcesso, @"\D", "");
            Processo? proc = null;
            if (_processoRepository != null && !string.IsNullOrWhiteSpace(digitos))
            {
                proc = await _processoRepository.ObterPorNumeroAsync(digitos, ct);
            }

            var classe = !string.IsNullOrWhiteSpace(req.ClasseProcessual) ? req.ClasseProcessual : proc?.Classe;
            var assunto = !string.IsNullOrWhiteSpace(req.AssuntoPrincipal) ? req.AssuntoPrincipal : proc?.Assunto;
            var orgao = !string.IsNullOrWhiteSpace(req.OrgaoJulgador) ? req.OrgaoJulgador : proc?.OrgaoJulgador;
            var poloAtivo = !string.IsNullOrWhiteSpace(req.PoloAtivo)
                ? req.PoloAtivo
                : proc?.Envolvidos?.FirstOrDefault(p =>
                    (p.TipoPolo ?? "").Contains("Ativo", StringComparison.OrdinalIgnoreCase) ||
                    (p.Papel ?? "").Contains("Agravante", StringComparison.OrdinalIgnoreCase) ||
                    (p.Papel ?? "").Contains("Autor", StringComparison.OrdinalIgnoreCase))?.Nome;
            var poloPassivo = !string.IsNullOrWhiteSpace(req.PoloPassivo)
                ? req.PoloPassivo
                : proc?.Envolvidos?.FirstOrDefault(p =>
                    (p.TipoPolo ?? "").Contains("Passivo", StringComparison.OrdinalIgnoreCase) ||
                    (p.Papel ?? "").Contains("Agravado", StringComparison.OrdinalIgnoreCase) ||
                    (p.Papel ?? "").Contains("Réu", StringComparison.OrdinalIgnoreCase))?.Nome;
            var ultAndamento = !string.IsNullOrWhiteSpace(req.UltimoAndamento)
                ? req.UltimoAndamento
                : proc?.Movimentacoes?.OrderByDescending(m => m.DataHora).FirstOrDefault()?.Descricao;

            DateOnly? dataDisp = null;
            if (!string.IsNullOrWhiteSpace(req.DataDisponibilizacaoIso) &&
                DateOnly.TryParse(req.DataDisponibilizacaoIso[..Math.Min(10, req.DataDisponibilizacaoIso.Length)], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtParsed))
            {
                dataDisp = dtParsed;
            }
            else if (proc?.Movimentacoes?.Count > 0)
            {
                dataDisp = DateOnly.FromDateTime(proc.Movimentacoes.OrderByDescending(m => m.DataHora).First().DataHora);
            }

            var feriadosLocais = estado.SuspensoesLocais.Select(s => new FeriadoOuSuspensao(s.Data, s.Descricao)).ToList();

            var minuta = GeradorMinutaProcessualService.GerarMinutaContextualizada(
                tenantId: tenant,
                numeroProcesso: req.NumeroProcesso,
                classeProcessual: classe,
                assuntoPrincipal: assunto,
                orgaoJulgador: orgao,
                poloAtivo: poloAtivo,
                poloPassivo: poloPassivo,
                ultimoAndamento: ultAndamento,
                dataDisponibilizacaoDjen: dataDisp,
                diasPrazo: req.DiasPrazo > 0 ? req.DiasPrazo : 15,
                tese: tese,
                configEscritorio: estado.Configuracao,
                suspensoesLocais: feriadosLocais);

            return Ok(minuta);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    // =========================================================================
    // FATIA 9: CALCULADORA FINANCEIRA & HONORÁRIOS BACEN (LEI 14.905/2024)
    // =========================================================================
    [HttpGet("processos/calculo-financeiro")]
    public async Task<IActionResult> CalcularAtualizacaoFinanceiraProcesso(
        [FromQuery] string numeroProcesso,
        [FromQuery] decimal valorOriginal = 157882.05m,
        [FromQuery] string? dataBaseIso = null,
        [FromQuery] string? dataFinalIso = null,
        CancellationToken ct = default)
    {
        if (_inteligenciaConnector is null)
        {
            return StatusCode(503, new { mensagem = "Conector de inteligência pública não configurado." });
        }

        var dtBase = new DateOnly(2025, 4, 1);
        if (!string.IsNullOrWhiteSpace(dataBaseIso) &&
            DateOnly.TryParse(dataBaseIso[..Math.Min(10, dataBaseIso.Length)], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedBase))
        {
            dtBase = parsedBase;
        }

        DateOnly? dtFim = null;
        if (!string.IsNullOrWhiteSpace(dataFinalIso) &&
            DateOnly.TryParse(dataFinalIso[..Math.Min(10, dataFinalIso.Length)], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedFim))
        {
            dtFim = parsedFim;
        }

        var resultado = await _inteligenciaConnector.CalcularAtualizacaoFinanceiraBacenAsync(
            numeroProcesso ?? "0000000-00.0000.0.00.0000",
            valorOriginal,
            dtBase,
            dtFim,
            ct);

        return Ok(resultado);
    }
}
