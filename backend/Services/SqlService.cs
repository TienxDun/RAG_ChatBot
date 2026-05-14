using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Backend.Services;

public sealed class SqlService
{
    private readonly IConfiguration _configuration;
    private readonly string _connectionString;
    
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    
    // Danh sách các từ khóa nguy hiểm bị cấm tuyệt đối
    private static readonly string[] ForbiddenKeywords = { 
        "DROP", "DELETE", "TRUNCATE", "ALTER", "UPDATE", "INSERT", 
        "EXEC", "EXECUTE", "CREATE", "GRANT", "REVOKE", "DBCC" 
    };

    public SqlService(IConfiguration configuration)
    {
        _configuration = configuration;
        _connectionString = _configuration["MSSQL_CONNECTION_STRING"] 
            ?? throw new InvalidOperationException("MSSQL_CONNECTION_STRING is not set in environment variables or configuration.");
    }

    public async Task<DataTable> ExecuteQueryAsDataTableAsync(string sql, CancellationToken ct)
    {

        ValidateSqlSecurity(sql);
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        using var command = new SqlCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync(ct);
        
        var dataTable = new DataTable();
        bool hasMoreResults = true;
        while (hasMoreResults)
        {
            var nextTable = new DataTable();
            nextTable.Load(reader);
            dataTable.Merge(nextTable);
            hasMoreResults = !reader.IsClosed;
        }

        return dataTable;

    }

    public async Task<string> ExecuteQueryAsJsonAsync(string sql, CancellationToken ct)
    {
        // 1. Kiểm tra an toàn trước khi thực thi
        ValidateSqlSecurity(sql);

        try 
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            using var command = new SqlCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync(ct);

            var results = new List<Dictionary<string, object>>();
            
            do 
            {
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
            } while (await reader.NextResultAsync(ct));

            // Tự động lọc bỏ các dòng trùng lặp hoàn toàn để kết quả sạch hơn
            var uniqueResults = results
                .GroupBy(r => JsonSerializer.Serialize(r))
                .Select(g => g.First())
                .ToList();

            return JsonSerializer.Serialize(uniqueResults, _jsonOptions);
        }
        catch (Exception ex)
        {
            throw new Exception($"SQL Execution Error: {ex.Message}", ex);
        }
    }

    private void ValidateSqlSecurity(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException("SQL query is empty.");

        var upperSql = sql.Trim().ToUpper();

        // 1. Chỉ cho phép câu lệnh truy vấn dữ liệu (SELECT hoặc CTE)
        if (!upperSql.StartsWith("SELECT") && !upperSql.StartsWith("WITH"))
        {
            throw new InvalidOperationException("Hệ thống chỉ cho phép thực thi các câu lệnh truy vấn dữ liệu (SELECT).");
        }

        // 2. Chặn chạy nhiều câu lệnh nguy hiểm, nhưng cho phép phân tách các câu lệnh SELECT/WITH bằng dấu ;
        if (sql.Contains(";") && (upperSql.Contains("DROP") || upperSql.Contains("DELETE") || upperSql.Contains("UPDATE") || upperSql.Contains("INSERT")))
        {
            throw new InvalidOperationException("Không được phép sử dụng dấu chấm phẩy (;) kết hợp với các lệnh thay đổi dữ liệu.");
        }

        // 3. Kiểm tra các từ khóa nguy hiểm
        foreach (var keyword in ForbiddenKeywords)
        {
            // Sử dụng Regex để tránh chặn nhầm các từ nằm trong tên cột (ví dụ: UpdateDate)
            var pattern = $@"\b{keyword}\b";
            if (Regex.IsMatch(upperSql, pattern))
            {
                throw new InvalidOperationException($"Phát hiện từ khóa nguy hiểm bị cấm: {keyword}");
            }
        }
    }
}