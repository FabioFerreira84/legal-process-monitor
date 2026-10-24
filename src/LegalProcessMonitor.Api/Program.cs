using System;
using LegalProcessMonitor.Domain.Interfaces;
using LegalProcessMonitor.Infrastructure.Configuration;
using LegalProcessMonitor.Infrastructure.Connectors.DataJud;
using LegalProcessMonitor.Infrastructure.Repositories;
using Microsoft.AspNetCore.Builder;
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

// Configuração de CORS para o Frontend Angular (Localhost e GitHub Pages)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularDev", policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",
                "https://fabioferreira84.github.io"
              )
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Registro do Cliente HTTP do DataJud com Resilience e Scraper do e-SAJ
builder.Services.AddHttpClient<ITribunalConnector, DataJudConnector>();
builder.Services.AddSingleton<ITribunalDocumentScraper, LegalProcessMonitor.Infrastructure.Connectors.Esaj.EsajDocumentScraper>();
builder.Services.AddSingleton<IEsajOabScraper, LegalProcessMonitor.Infrastructure.Connectors.Esaj.EsajOabScraper>();
builder.Services.AddSingleton<IMagistradoRepository, InMemoryMagistradoRepository>();

// Configuração de Persistência (MongoDB com Fallback gracioso In-Memory)
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

        // Ping síncrono rápido para validar conectividade imediata
        db.RunCommand<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));

        logger.LogInformation("Conectado ao MongoDB com sucesso em {DatabaseName}", mongoSettings.DatabaseName);
        return new MongoProcessoRepository(db, sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MongoSettings>>(), logger);
    }
    catch (System.Exception ex)
    {
        logger.LogWarning("MongoDB offline no endereço configurado ({Msg}). Ativando repositório In-Memory para desenvolvimento.", ex.Message);
        return new InMemoryProcessoRepository();
    }
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowAngularDev");
app.MapControllers();

app.Run();
