using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace Backend.Services;

public sealed class SqlService
{
    private readonly IConfiguration _configuration;
    private readonly string _connectionString;

    public SqlService(IConfiguration configuration)
    {
        _configuration = configuration;
        // Ưu tiên lấy từ IConfiguration (nó bao gồm cả Environment Variables)
        _connectionString = _configuration["MSSQL_CONNECTION_STRING"] 
            ?? throw new InvalidOperationException("MSSQL_CONNECTION_STRING is not set in environment variables or configuration.");
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
