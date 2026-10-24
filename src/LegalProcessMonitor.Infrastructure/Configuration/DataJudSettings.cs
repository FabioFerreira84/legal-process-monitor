namespace LegalProcessMonitor.Infrastructure.Configuration;

public class DataJudSettings
{
    public const string SectionName = "DataJud";

    /// <summary>
    /// Chave pública oficial disponibilizada pelo CNJ para consulta aberta de dados públicos.
    /// </summary>
    public string ApiKey { get; set; } = "cDZHYzlZa0JadVREZDJCendQbXY6SkJlTzNjLV9TRENyQk1RdnFKZGRQdw==";
    public string BaseUrl { get; set; } = "https://api-publica.datajud.cnj.jus.br";
    public int TimeoutSegundos { get; set; } = 30;
    public int TentativasRetry { get; set; } = 3;
}
