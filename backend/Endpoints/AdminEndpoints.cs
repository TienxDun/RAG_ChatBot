using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Backend.Endpoints;

public static class AdminEndpoints
{
    public static void MapRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin");

        // Login API
        group.MapPost("/login", (LoginRequest request) =>
        {
            if (request.Username == "admin" && request.Password == "admin")
            {
                return Results.Ok(new { success = true, token = "dodo-admin-session-token-key-2026" });
            }
            return Results.BadRequest(new { success = false, message = "Tên đăng nhập hoặc mật khẩu không đúng." });
        });

        // Get Qdrant Collections
        group.MapGet("/qdrant/collections", async (QdrantService qdrantService) =>
        {
            try
            {
                var collections = await qdrantService.GetCollectionsAsync();
                return Results.Ok(collections);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Không thể lấy danh sách Qdrant collections: {ex.Message}" });
            }
        });

        // CRUD APIs for DataSources
        group.MapGet("/datasources", (DataSourceRegistry registry) =>
        {
            var datasources = registry.GetAll().Select(ds =>
            {
                string rawCS = "";
                try
                {
                    rawCS = registry.GetConnectionString(ds);
                }
                catch { }

                bool hasCS = !string.IsNullOrEmpty(rawCS);
                string preview = MaskConnectionString(rawCS);

                return new DataSourceAdminDto(
                    ds.Id,
                    ds.DisplayName,
                    ds.Description,
                    ds.QdrantCollection,
                    ds.RulesFolder,
                    ds.IsDefault,
                    hasCS,
                    preview
                );
            });

            return Results.Ok(datasources);
        });

        group.MapPost("/datasources", (CreateDataSourceRequest request, DataSourceRegistry registry) =>
        {
            if (string.IsNullOrWhiteSpace(request.Id) || !Regex.IsMatch(request.Id, "^[a-zA-Z0-9_]+$"))
            {
                return Results.BadRequest(new { error = "ID không hợp lệ. Chỉ chấp nhận chữ cái, số và dấu gạch dưới." });
            }

            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return Results.BadRequest(new { error = "Tên hiển thị không được để trống." });
            }

            if (string.IsNullOrWhiteSpace(request.QdrantCollection))
            {
                return Results.BadRequest(new { error = "Qdrant Collection không được để trống." });
            }

            var newSource = new DataSourceConfig
            {
                Id = request.Id.ToLowerInvariant(),
                DisplayName = request.DisplayName,
                Description = request.Description ?? "",
                QdrantCollection = request.QdrantCollection,
                ConnectionString = request.ConnectionString ?? "",
                RulesFolder = request.RulesFolder,
                IsDefault = request.IsDefault
            };

            try
            {
                registry.AddDataSource(newSource);
                
                var absoluteRulesPath = Path.Combine(Directory.GetCurrentDirectory(), newSource.RulesFolder);
                return Results.Ok(new { 
                    message = "Thêm nguồn dữ liệu thành công.",
                    rulesFolderPath = absoluteRulesPath
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPut("/datasources/{id}", (string id, CreateDataSourceRequest request, DataSourceRegistry registry) =>
        {
            var existing = registry.GetById(id);
            if (existing == null)
            {
                return Results.NotFound(new { error = "Không tìm thấy nguồn dữ liệu." });
            }

            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return Results.BadRequest(new { error = "Tên hiển thị không được để trống." });
            }

            if (string.IsNullOrWhiteSpace(request.QdrantCollection))
            {
                return Results.BadRequest(new { error = "Qdrant Collection không được để trống." });
            }

            // If user did not provide a new connection string (masked or empty), preserve the existing one
            string? newCS = request.ConnectionString;
            if (string.IsNullOrEmpty(newCS) || newCS.Contains("***"))
            {
                newCS = existing.ConnectionString;
            }

            var updated = new DataSourceConfig
            {
                Id = id,
                DisplayName = request.DisplayName,
                Description = request.Description ?? "",
                QdrantCollection = request.QdrantCollection,
                ConnectionString = newCS,
                RulesFolder = request.RulesFolder,
                IsDefault = request.IsDefault
            };

            try
            {
                registry.UpdateDataSource(id, updated);

                var absoluteRulesPath = Path.Combine(Directory.GetCurrentDirectory(), updated.RulesFolder);
                return Results.Ok(new { 
                    message = "Cập nhật nguồn dữ liệu thành công.",
                    rulesFolderPath = absoluteRulesPath
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapDelete("/datasources/{id}", (string id, DataSourceRegistry registry) =>
        {
            try
            {
                var deleted = registry.RemoveDataSource(id);
                if (deleted)
                {
                    return Results.Ok(new { message = "Xóa nguồn dữ liệu thành công." });
                }
                return Results.NotFound(new { error = "Không tìm thấy nguồn dữ liệu." });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPost("/datasources/test", async (TestConnectionRequest request, DataSourceRegistry registry) =>
        {
            if (string.IsNullOrWhiteSpace(request.ConnectionString))
            {
                return Results.BadRequest(new { success = false, message = "ConnectionString không được để trống." });
            }

            try
            {
                string connectionString = request.ConnectionString;

                // Nếu ConnectionString thực chất là ID của DataSource, tự động resolve Connection String thực tế
                var ds = registry.GetById(connectionString);
                if (ds != null)
                {
                    connectionString = registry.GetConnectionString(ds);
                }

                using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();

                // Get database name and count tables
                string dbName = conn.Database;
                int tableCount = 0;

                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'", conn))
                {
                    var result = await cmd.ExecuteScalarAsync();
                    tableCount = Convert.ToInt32(result);
                }

                return Results.Ok(new
                {
                    success = true,
                    message = $"Kết nối thành công. Database: {dbName}. Tổng số bảng: {tableCount}.",
                    databaseName = dbName,
                    tableCount = tableCount
                });
            }
            catch (Exception ex)
            {
                return Results.Ok(new
                {
                    success = false,
                    message = $"Kết nối thất bại: {ex.Message}"
                });
            }
        });
    }

    private static string MaskConnectionString(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return "";

        // Regex helpers to replace password, server IP/address, and user ID
        var masked = connectionString;
        
        // Mask Password
        masked = Regex.Replace(masked, @"(Password|Pwd)\s*=\s*[^;]+", "$1=********", RegexOptions.IgnoreCase);
        
        // Mask Server/IP
        masked = Regex.Replace(masked, @"(Server|Data Source|Addr)\s*=\s*([^;]+)", (match) =>
        {
            var serverPart = match.Groups[2].Value;
            if (serverPart.Contains(",")) // IP with port
            {
                var parts = serverPart.Split(',');
                return $"{match.Groups[1].Value}={MaskIPOrAddress(parts[0])},{parts[1]}";
            }
            return $"{match.Groups[1].Value}={MaskIPOrAddress(serverPart)}";
        }, RegexOptions.IgnoreCase);

        return masked;
    }

    private static string MaskIPOrAddress(string input)
    {
        if (IPAddressExists(input))
        {
            var segments = input.Split('.');
            if (segments.Length == 4)
            {
                return $"{segments[0]}.{segments[1]}.***.***";
            }
        }
        
        // For hostnames, mask the first part
        if (input.Length > 4)
        {
            return $"***{input.Substring(input.Length - 4)}";
        }
        return "***";
    }

    private static bool IPAddressExists(string text)
    {
        return Regex.IsMatch(text, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$");
    }
}
