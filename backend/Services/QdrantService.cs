using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Backend.Services;

public sealed class QdrantService
{
    private readonly QdrantClient _client;
    private const string CollectionName = "db_schema";

    public QdrantService()
    {
        var host = Environment.GetEnvironmentVariable("QDRANT_HOST") ?? "localhost";
        var apiKey = Environment.GetEnvironmentVariable("QDRANT_API_KEY");
        
        // Nếu host không phải localhost thì mặc định dùng HTTPS (cho Cloud)
        bool useHttps = host != "localhost";
        
        _client = new QdrantClient(host, port: 6334, https: useHttps, apiKey: apiKey);
    }

    public async Task<List<string>> SearchSchemaAsync(IReadOnlyList<float> vector, int limit = 3)
    {
        var searchResult = await _client.SearchAsync(
            CollectionName,
            vector: vector.ToArray(),
            limit: (uint)limit
        );

        return searchResult.Select(r => r.Payload["full_text"].StringValue).ToList();
    }

    public async Task UpsertPointsAsync(
        List<(IReadOnlyList<float> Vector, string Text, string FileName, int Index)> points,
        CancellationToken ct)
    {
        if (points.Count == 0) return;

        var collections = await _client.ListCollectionsAsync(cancellationToken: ct);
        if (!collections.Contains(CollectionName))
        {
            var vectorSize = (ulong)points.First().Vector.Count;
            await _client.CreateCollectionAsync(
                CollectionName,
                new VectorParams { Size = vectorSize, Distance = Distance.Cosine },
                cancellationToken: ct
            );
        }

        var pointStructs = points.Select(p => 
        {
            // Tạo ID số cố định dựa trên tên file và index
            var numericId = CreateNumericId(p.FileName, p.Index);
            
            return new PointStruct
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
        }).ToList();

        await _client.UpsertAsync(CollectionName, pointStructs, cancellationToken: ct);
    }

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
