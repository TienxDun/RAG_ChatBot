namespace Backend.Models;

public sealed class DataSourceConfig
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string QdrantCollection { get; set; } = string.Empty;
    public string ConnectionStringEnvVar { get; set; } = string.Empty;
    public string? ConnectionString { get; set; } = string.Empty;
    public string RulesFolder { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

