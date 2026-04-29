using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace Backend.Services;

public sealed class SqlService
{
    private readonly string _connectionString;

    public SqlService()
    {
        var password = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD") ?? "YourStrong@Password123";
        var user = Environment.GetEnvironmentVariable("MSSQL_USER") ?? "sa";
        var port = Environment.GetEnvironmentVariable("MSSQL_PORT") ?? "1433";
        var db = Environment.GetEnvironmentVariable("MSSQL_DATABASE") ?? "GarmentDB";
        
        // Dùng localhost vì Backend chạy local ngoài Docker
        _connectionString = $"Server=localhost,{port};Database={db};User Id={user};Password={password};TrustServerCertificate=True";
    }

    public async Task<string> ExecuteQueryAsJsonAsync(string sql, CancellationToken ct)
    {
        try 
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            using var command = new SqlCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync(ct);

            var results = new List<Dictionary<string, object>>();
            var columnNames = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();

            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object>();
                foreach (var name in columnNames)
                {
                    var value = reader[name];
                    row[name] = value is DBNull ? null! : value;
                }
                results.Add(row);
            }

            return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            throw new Exception($"SQL Execution Error: {ex.Message}", ex);
        }
    }
}
