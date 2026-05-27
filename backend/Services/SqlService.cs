using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Backend.Services.Security;

namespace Backend.Services;

public sealed class SqlService
{
    private readonly string _connectionString;
    private readonly ISqlSecurityValidator _securityValidator;
    
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // Khởi tạo SqlService bằng cách lấy chuỗi kết nối từ SqlOptions và tiêm ISqlSecurityValidator.
    public SqlService(Backend.Models.SqlOptions options, ISqlSecurityValidator securityValidator)
    {
        _connectionString = options.ConnectionString;
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("MSSQL_CONNECTION_STRING is not set in configuration.");
        }
        _securityValidator = securityValidator;
    }

    // Thực thi câu lệnh SQL truy vấn và trả về kết quả dưới dạng cấu trúc bảng DataTable của ADO.NET.
    public async Task<DataTable> ExecuteQueryAsDataTableAsync(string sql, CancellationToken ct)
    {
        // Kiểm tra an toàn bảo mật của câu lệnh trước khi thực thi
        _securityValidator.ValidateSqlSecurity(sql);
        
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

    // Thực thi câu lệnh SQL truy vấn và trả về kết quả dưới dạng chuỗi JSON đã được format và lọc trùng lặp.
    // Phục vụ việc xuất hoặc truyền nhận dữ liệu phi cấu trúc một cách nhanh chóng.
    public async Task<string> ExecuteQueryAsJsonAsync(string sql, CancellationToken ct)
    {
        // 1. Kiểm tra an toàn trước khi thực thi
        _securityValidator.ValidateSqlSecurity(sql);

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
}