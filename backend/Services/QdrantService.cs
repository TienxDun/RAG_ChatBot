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
}
