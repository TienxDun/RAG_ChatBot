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
    private readonly string _vikingConnectionString;
    private readonly ISqlSecurityValidator _securityValidator;
    
    // Khởi tạo SqlService bằng cách lấy chuỗi kết nối từ SqlOptions và tiêm ISqlSecurityValidator.
    public SqlService(Backend.Models.SqlOptions options, ISqlSecurityValidator securityValidator)
    {
        _connectionString = options.ConnectionString;
        _vikingConnectionString = options.VikingConnectionString;
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("MSSQL_CONNECTION_STRING is not set in configuration.");
        }
        if (string.IsNullOrWhiteSpace(_vikingConnectionString))
        {
            throw new InvalidOperationException("MSSQL_VIKING_CONNECTION_STRING is not set in configuration.");
        }
        _securityValidator = securityValidator;
    }

    private string GetConnectionString(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return _connectionString;

        // List of Viking tables
        var vikingTables = new[] 
        { 
            "BangSize", "ChungLoaiVatTu", "ERP_ChiTietNhapKhoNPL", "ERP_DonViVT", 
            "ERP_KhachHangNK", "ERP_KhoVai", "ERP_MauVTTV", "ERP_NhapKhoNPL", 
            "ERP_TheKhoVai", "ERP_VatTuTV", "KhachHang", "Qty_KiemPL", 
            "Qty_KiemPL_XacNhan", "QTY_PhuLucKiemPL" 
        };

        foreach (var table in vikingTables)
        {
            if (sql.Contains(table, StringComparison.OrdinalIgnoreCase))
            {
                return _vikingConnectionString;
            }
        }

        return _connectionString;
    }

    // Thực thi câu lệnh SQL truy vấn và trả về kết quả dưới dạng cấu trúc bảng DataTable của ADO.NET.
    public async Task<DataTable> ExecuteQueryAsDataTableAsync(string sql, CancellationToken ct)
    {
        // Kiểm tra an toàn bảo mật của câu lệnh trước khi thực thi
        _securityValidator.ValidateSqlSecurity(sql);
        
        var connStr = GetConnectionString(sql);
        using var connection = new SqlConnection(connStr);
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
    public async Task<List<Dictionary<string, object?>>> QueryAsync(string sql, CancellationToken ct)
    {
        var connStr = GetConnectionString(sql);
        using var connection = new SqlConnection(connStr);
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