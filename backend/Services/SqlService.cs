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
    private readonly ISqlSecurityValidator _securityValidator;
    
    // Khởi tạo SqlService bằng cách tiêm ISqlSecurityValidator.
    public SqlService(ISqlSecurityValidator securityValidator)
    {
        _securityValidator = securityValidator;
    }

    // Thực thi câu lệnh SQL truy vấn và trả về kết quả dưới dạng cấu trúc bảng DataTable của ADO.NET.
    public async Task<DataTable> ExecuteQueryAsDataTableAsync(string sql, string connectionString, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
        }

        // Kiểm tra an toàn bảo mật của câu lệnh trước khi thực thi
        _securityValidator.ValidateSqlSecurity(sql);
        
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = 25; // Giới hạn 25s — thấp hơn pipeline timeout 60s để query treo sẽ bị hủy trước
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

    /// Thực thi câu lệnh SQL nội bộ (whitelist) và trả về list of dict.
    /// Dùng cho data lookup endpoint — SQL được build từ whitelist bảng/cột nên không cần security validator.
    public async Task<List<Dictionary<string, object?>>> QueryAsync(string sql, string connectionString, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
        }

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = 10;
        using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var colName = reader.GetName(i);
                row[colName] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            results.Add(row);
        }
        return results;
    }
}