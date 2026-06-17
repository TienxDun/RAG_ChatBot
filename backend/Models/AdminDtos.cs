using System.Collections.Generic;

namespace Backend.Models;

public record LoginRequest(string Username, string Password);

public record CreateDataSourceRequest(
    string Id,
    string DisplayName,
    string Description,
    string QdrantCollection,
    string? ConnectionString,
    string RulesFolder,
    bool IsDefault
);

public record TestConnectionRequest(string ConnectionString);

public record DataSourceAdminDto(
    string Id,
    string DisplayName,
    string Description,
    string QdrantCollection,
    string RulesFolder,
    bool IsDefault,
    bool HasConnectionString,
    string ConnectionStringPreview
);
