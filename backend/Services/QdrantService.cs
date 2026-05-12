using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Backend.Services;

public sealed class QdrantService
{
    private readonly QdrantClient _client;
    private const string DefaultCollectionName = "db_schema";

    public QdrantService()
    {
        var host = Environment.GetEnvironmentVariable("QDRANT_HOST") ?? "localhost";
        var apiKey = Environment.GetEnvironmentVariable("QDRANT_API_KEY");
        
        // Nếu host không phải localhost thì mặc định dùng HTTPS (cho Cloud)
        bool useHttps = host != "localhost";
        
        _client = new QdrantClient(host, port: 6334, https: useHttps, apiKey: apiKey);
    }

    public async Task<List<string>> GetCollectionsAsync()
    {
        var collections = await _client.ListCollectionsAsync();
        return collections.ToList();
    }

    public async Task<List<string>> SearchSchemaAsync(IReadOnlyList<float> vector, int limit = 3, string? collectionName = null)
    {
        var targetCollection = string.IsNullOrWhiteSpace(collectionName) ? DefaultCollectionName : collectionName;
        
        var searchResult = await _client.SearchAsync(
            targetCollection,
            vector: vector.ToArray(),
            limit: (uint)limit
        );

        return searchResult.Select(r => r.Payload["full_text"].StringValue).ToList();
    }

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
        }
        else
        {
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
