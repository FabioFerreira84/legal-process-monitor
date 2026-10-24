namespace LegalProcessMonitor.Infrastructure.Configuration;

public class DataJudSettings
{
    public const string SectionName = "DataJud";

    /// <summary>
    /// Chave pública oficial disponibilizada pelo CNJ para consulta aberta de dados públicos.
    /// </summary>
    public string ApiKey { get; set; } = "cDZHYzlZa0JadVREZDJCendQbXNpOFk2UG1zTjo4ZjhKMnF6WFR1cTY4TGNwYmd3V1pR";
    public string BaseUrl { get; set; } = "https://api-publica.datajud.cnj.jus.br";
    public int TimeoutSegundos { get; set; } = 30;
    public int TentativasRetry { get; set; } = 3;
}
