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

// Configuração de CORS para o Frontend Angular
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularDev", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Registro do Cliente HTTP do DataJud com Resilience
builder.Services.AddHttpClient<ITribunalConnector, DataJudConnector>();

// Configuração de Persistência (MongoDB com Fallback gracioso In-Memory)
var mongoSettings = builder.Configuration.GetSection(MongoSettings.SectionName).Get<MongoSettings>() ?? new MongoSettings();
builder.Services.AddSingleton<IProcessoRepository>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<MongoProcessoRepository>>();
    try
    {
        var client = new MongoClient(mongoSettings.ConnectionString);
        // Ping rápido para verificar conectividade
        var db = client.GetDatabase(mongoSettings.DatabaseName);
        db.RunCommandAsync((Command<MongoDB.Bson.BsonDocument>)"{ping:1}").Wait(1500);

        logger.LogInformation("Conectado ao MongoDB com sucesso em {DatabaseName}", mongoSettings.DatabaseName);
        return new MongoProcessoRepository(db, sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MongoSettings>>(), logger);
    }
    catch (System.Exception ex)
    {
        logger.LogWarning("MongoDB não está acessível no momento ({Msg}). Utilizando repositório In-Memory para desenvolvimento e testes.", ex.Message);
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
