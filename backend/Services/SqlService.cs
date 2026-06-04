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
}