namespace LegalProcessMonitor.Infrastructure.Configuration;

public class MongoSettings
{
    public const string SectionName = "MongoDB";

    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string DatabaseName { get; set; } = "LegalProcessMonitorDb";
    public string CollectionProcessos { get; set; } = "processos";
}
