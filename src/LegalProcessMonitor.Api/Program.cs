using System;
using System.IO;
using System.Threading.RateLimiting;
using LegalProcessMonitor.Domain.Interfaces;
using LegalProcessMonitor.Infrastructure.Configuration;
using LegalProcessMonitor.Infrastructure.Connectors.Alerts;
using LegalProcessMonitor.Infrastructure.Connectors.DataJud;
using LegalProcessMonitor.Infrastructure.Connectors.Djen;
using LegalProcessMonitor.Infrastructure.Connectors.Esaj;
using LegalProcessMonitor.Infrastructure.Connectors.Inteligencia;
using LegalProcessMonitor.Infrastructure.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Configurações
builder.Services.Configure<DataJudSettings>(builder.Configuration.GetSection(DataJudSettings.SectionName));
builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection(MongoSettings.SectionName));

// Configuração de Controllers e OpenAPI
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Rate Limiting por IP para proteger conectores externos (DataJud, DJEN e e-SAJ) contra abuso
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 30,
                QueueLimit = 2,
                Window = TimeSpan.FromMinutes(1)
            });
    });
});

// Configuração de CORS para o Frontend Angular (Localhost, GitHub Pages e domínios extras via CORS_ALLOWED_ORIGINS)
var extraOrigins = (builder.Configuration["CORS_ALLOWED_ORIGINS"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var allowedOrigins = new[] { "http://localhost:4200", "http://localhost:8080", "https://fabioferreira84.github.io" }
    .Concat(extraOrigins)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularDev", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Registro dos Clientes HTTP e Conectores Oficiais (DataJud CNJ, DJEN/PJe CNJ e e-SAJ)
builder.Services.AddHttpClient<ITribunalConnector, DataJudConnector>();
builder.Services.AddHttpClient<IDjenComunicacaoConnector, DjenComunicacaoConnector>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});
builder.Services.AddSingleton<ITribunalDocumentScraper, EsajDocumentScraper>();
builder.Services.AddSingleton<IEsajOabScraper, EsajOabScraper>();
builder.Services.AddSingleton<IMagistradoRepository, InMemoryMagistradoRepository>();
builder.Services.AddHttpClient<IInteligenciaPublicaConnector, InteligenciaPublicaConnector>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(12);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("User-Agent", "LegalProcessMonitor/2.5 (+https://github.com/FabioFerreira84/legal-process-monitor)");
});
builder.Services.AddHttpClient<IWebhookAlertDispatcher, WebhookAlertDispatcher>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
});

// Persistência Relacional Multi-Tenant em SQLite (Modo WAL)
var sqlitePath = builder.Configuration["Sqlite:DatabasePath"]
    ?? Path.Combine(AppContext.BaseDirectory, "data", "legalmonitor.db");
var sqliteDir = Path.GetDirectoryName(sqlitePath);
if (!string.IsNullOrWhiteSpace(sqliteDir))
{
    Directory.CreateDirectory(sqliteDir);
}
var sqliteConnString = $"Data Source={sqlitePath}";

builder.Services.AddSingleton<IWorkspaceRepository>(_ =>
    new SqliteWorkspaceRepository(sqliteConnString, semearDemoPadrao: true));

// Configuração de Persistência de Processos (MongoDB com Fallback Relacional Durável em SQLite)
var mongoSettings = builder.Configuration.GetSection(MongoSettings.SectionName).Get<MongoSettings>() ?? new MongoSettings();
builder.Services.AddSingleton<IProcessoRepository>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<MongoProcessoRepository>>();
    try
    {
        var settings = MongoClientSettings.FromConnectionString(mongoSettings.ConnectionString);
        settings.ServerSelectionTimeout = TimeSpan.FromMilliseconds(1000);
        settings.ConnectTimeout = TimeSpan.FromMilliseconds(1000);

        var client = new MongoClient(settings);
        var db = client.GetDatabase(mongoSettings.DatabaseName);

        db.RunCommand<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));

        logger.LogInformation("Conectado ao MongoDB com sucesso em {DatabaseName}", mongoSettings.DatabaseName);
        return new MongoProcessoRepository(db, sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MongoSettings>>(), logger);
    }
    catch (Exception ex)
    {
        logger.LogInformation("MongoDB não configurado ({Msg}). Ativando repositório relacional SQLite durável em {SqlitePath}.", ex.Message, sqlitePath);
        return new SqliteProcessoRepository(sqliteConnString);
    }
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Security Headers Middleware
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseCors("AllowAngularDev");
app.UseRateLimiter();

// Endpoint de Healthcheck para Docker / Render / Fly.io / Load Balancers
app.MapGet("/healthz", async (IWorkspaceRepository workspaceRepo) =>
{
    var estado = await workspaceRepo.ObterEstadoWorkspaceAsync("escritorio-ferreira");
    return Results.Ok(new
    {
        status = "healthy",
        service = "LegalProcessMonitor.Api",
        storage = "SQLite-WAL",
        watchlistDemoItens = estado.Watchlist.Count,
        timestampUtc = DateTime.UtcNow
    });
});

app.MapControllers();

app.Run();
