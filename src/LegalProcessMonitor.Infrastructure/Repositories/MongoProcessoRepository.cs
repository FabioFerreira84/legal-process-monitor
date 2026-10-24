using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LegalProcessMonitor.Domain.Entities;
using LegalProcessMonitor.Domain.Interfaces;
using LegalProcessMonitor.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace LegalProcessMonitor.Infrastructure.Repositories;

public class MongoProcessoRepository : IProcessoRepository
{
    private readonly IMongoCollection<Processo> _collection;
    private readonly ILogger<MongoProcessoRepository> _logger;

    public MongoProcessoRepository(
        IMongoDatabase database,
        IOptions<MongoSettings> settings,
        ILogger<MongoProcessoRepository> logger)
    {
        _logger = logger;
        _collection = database.GetCollection<Processo>(settings.Value.CollectionProcessos);

        CriarIndicesAsync();
    }

    private void CriarIndicesAsync()
    {
        try
        {
            var indexKeys = Builders<Processo>.IndexKeys.Ascending(p => p.NumeroCnjLimpo);
            var indexModel = new CreateIndexModel<Processo>(indexKeys, new CreateIndexOptions { Unique = true });
            _collection.Indexes.CreateOne(indexModel);
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning("Não foi possível criar índice único no MongoDB (pode estar offline ou sem permissão): {Message}", ex.Message);
        }
    }

    public async Task<Processo?> ObterPorNumeroAsync(string numeroCnjLimpo, CancellationToken ct = default)
    {
        var filter = Builders<Processo>.Filter.Eq(p => p.NumeroCnjLimpo, numeroCnjLimpo);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task SalvarOuAtualizarAsync(Processo processo, CancellationToken ct = default)
    {
        var filter = Builders<Processo>.Filter.Eq(p => p.NumeroCnjLimpo, processo.NumeroCnjLimpo);
        var options = new ReplaceOptions { IsUpsert = true };

        await _collection.ReplaceOneAsync(filter, processo, options, ct);
        _logger.LogInformation("Processo {Numero} persistido com sucesso no MongoDB.", processo.NumeroCnjFormatado);
    }

    public async Task<IReadOnlyList<Processo>> ListarRecentesAsync(int limite = 50, CancellationToken ct = default)
    {
        return await _collection.Find(Builders<Processo>.Filter.Empty)
            .SortByDescending(p => p.UltimaAtualizacao)
            .Limit(limite)
            .ToListAsync(ct);
    }
}
