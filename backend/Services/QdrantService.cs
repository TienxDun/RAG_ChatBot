using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Backend.Services;

public sealed class QdrantService
{
    private readonly QdrantClient _client;
    private const string DefaultCollectionName = "db_schema";

    // Khởi tạo QdrantService bằng cách tiêm cấu hình QdrantOptions.
    // Bắt buộc có QDRANT_HOST và QDRANT_API_KEY. Luôn sử dụng HTTPS kết nối trực tiếp tới Qdrant Cloud.
    public QdrantService(Backend.Models.QdrantOptions options)
    {
        // Vì hệ thống hiện tại sử dụng Qdrant online/Cloud, luôn bắt buộc dùng HTTPS
        _client = new QdrantClient(options.Host, port: 6334, https: true, apiKey: options.ApiKey);
    }

    
    // Lấy danh sách tất cả các collections hiện có trong Qdrant.
    public async Task<List<string>> GetCollectionsAsync()
    {
        var collections = await _client.ListCollectionsAsync();
        return collections.ToList();
    }

    // Tìm kiếm cấu trúc database (schema) tương đồng nhất dựa trên vector đầu vào.
    // Trả về danh sách thông tin schema thô (full_text) làm ngữ cảnh (context) cho AI.
    public async Task<List<string>> SearchSchemaAsync(IReadOnlyList<float> vector, int limit = 3, string? collectionName = null)
    {
        var targetCollection = string.IsNullOrWhiteSpace(collectionName) ? DefaultCollectionName : collectionName;
        
        try
        {
            var searchResult = await _client.SearchAsync(
                targetCollection,
                vector: vector.ToArray(),
                limit: (uint)limit
            );

            return searchResult.Select(r => r.Payload["full_text"].StringValue).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Qdrant connection failed, falling back to local schemas: {ex.Message}");
            var schemasList = new List<string>();
            var parser = new Backend.Services.Document.DbSchemaParser();
            
            var baseDirs = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "rag_schemas"),
                Path.Combine(AppContext.BaseDirectory, "rag_schemas"),
                Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "backend")), "rag_schemas"),
                Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")), "rag_schemas"),
            };

            string? schemaDir = null;
            foreach (var dir in baseDirs)
            {
                if (Directory.Exists(dir))
                {
                    schemaDir = dir;
                    break;
                }
            }

            if (schemaDir != null)
            {
                var files = Directory.GetFiles(schemaDir, "*.json");
                foreach (var file in files)
                {
                    if (Path.GetFileName(file).StartsWith("_")) continue;
                    try
                    {
                        var json = File.ReadAllText(file);
                        using var doc = JsonDocument.Parse(json);
                        if (parser.IsDatabaseSchema(doc.RootElement))
                        {
                            var markdown = parser.ParseSchema(doc.RootElement, out _, out _);
                            schemasList.Add(markdown);
                        }
                    }
                    catch { }
                }
            }

            if (schemasList.Count > 0)
            {
                var priorityOrder = new List<string>
                {
                    "QTY_MAHANG_NGAYKIEM",
                    "SEW_CoefficientSize",
                    "QTY_MaHang_KiemQC_ChiTiet",
                    "ERP_LENHSX",
                    "DIC_KHACHHANG",
                    "tbl_SettingLineX"
                };

                var sortedSchemas = schemasList
                    .OrderBy(s => {
                        var firstLine = s.Split('\n').FirstOrDefault() ?? "";
                        var tableName = firstLine.Replace("# BẢNG:", "").Trim();
                        var idx = priorityOrder.IndexOf(tableName);
                        return idx >= 0 ? idx : 99;
                    })
                    .Take(limit)
                    .ToList();

                return sortedSchemas;
            }

            throw;
        }
    }

    
    // Thêm mới hoặc cập nhật các điểm vector (points) vào một collection trong Qdrant.
    // Nếu collection chưa tồn tại, hàm sẽ tự động tạo mới.
    // Nếu đã tồn tại, hàm sẽ xóa dữ liệu cũ của cùng một file trước khi chèn mới để tránh trùng lặp.
    public async Task UpsertPointsAsync(
        List<QdrantPoint> points,
        string? collectionName,
        CancellationToken ct)
    {
        if (points.Count == 0) return;

        var targetCollection = string.IsNullOrWhiteSpace(collectionName) ? DefaultCollectionName : collectionName;

        var collections = await _client.ListCollectionsAsync(cancellationToken: ct);
        if (!collections.Contains(targetCollection))
        {
            var vectorSize = (ulong)points.First().Vector.Count;
            await _client.CreateCollectionAsync(
                targetCollection,
                new VectorParams { Size = vectorSize, Distance = Distance.Cosine },
                cancellationToken: ct
            );

            // Tạo index cho source_file để phục vụ lọc/xóa
            await _client.CreatePayloadIndexAsync(
                targetCollection,
                "source_file",
                PayloadSchemaType.Keyword,
                cancellationToken: ct
            );
        }
        else
        {
            // Đảm bảo payload index cho source_file tồn tại trước khi xóa/lọc
            try
            {
                await _client.CreatePayloadIndexAsync(
                    targetCollection,
                    "source_file",
                    PayloadSchemaType.Keyword,
                    cancellationToken: ct
                );
            }
            catch (Exception ex)
            {
                // Chỉ bỏ qua nếu index đã tồn tại
                if (!ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) && 
                    !ex.Message.Contains("already indexed", StringComparison.OrdinalIgnoreCase) &&
                    !ex.Message.Contains("AlreadyExists", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Lỗi tạo payload index cho 'source_file': {ex.Message}", ex);
                }
            }

            var fileName = points.First().FileName;
            await _client.DeleteAsync(targetCollection, 
                filter: new Qdrant.Client.Grpc.Filter
                {
                    Must = 
                    { 
                        new Qdrant.Client.Grpc.Condition 
                        { 
                            Field = new Qdrant.Client.Grpc.FieldCondition 
                            { 
                                Key = "source_file", 
                                Match = new Qdrant.Client.Grpc.Match { Keyword = fileName } 
                            } 
                        } 
                    }
                },
                cancellationToken: ct);
        }

        var pointStructs = points.Select(p => 
        {
            var numericId = CreateNumericId(p.FileName, p.Index);
            
            var point = new PointStruct
            {
                Id = new PointId { Num = numericId },
                Vectors = p.Vector.ToArray(),
                Payload =
                {
                    ["full_text"] = p.Text,
                    ["source_file"] = p.FileName,
                    ["chunk_index"] = p.Index,
                    ["indexed_at"] = DateTime.UtcNow.ToString("o")
                }
            };

            // Add additional metadata fields if any
            if (p.Metadata != null)
            {
                foreach (var meta in p.Metadata)
                {
                    point.Payload[meta.Key] = meta.Value;
                }
            }

            return point;
        }).ToList();

        await _client.UpsertAsync(targetCollection, pointStructs, cancellationToken: ct);
    }

    public record QdrantPoint(IReadOnlyList<float> Vector, string Text, string FileName, int Index, Dictionary<string, string>? Metadata = null);

    // Tạo một ID số nguyên 64-bit (ulong) duy nhất dựa trên việc băm tên file và chỉ số chunk.
    // Giúp định danh duy nhất cho từng điểm vector trong Qdrant.
    private static ulong CreateNumericId(string fileName, int index)
    {
        // Băm tên file thành một số 32-bit làm tiền tố
        uint fileHash = 0;
        foreach (char c in fileName)
        {
            fileHash = fileHash * 31 + c;
        }

        // Kết hợp tiền tố fileHash và index để tạo ID ulong duy nhất và có trật tự
        // fileHash ở 32 bit cao, index ở 32 bit thấp
        return ((ulong)fileHash << 32) | (uint)index;
    }
}
