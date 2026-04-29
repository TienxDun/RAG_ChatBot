using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Backend.Services;

public sealed class QdrantService
{
    private readonly QdrantClient _client;
    private const string CollectionName = "db_schema";

    public QdrantService()
    {
        // Qdrant chạy trong Docker, map port 6333 ra localhost
        _client = new QdrantClient("localhost", 6334); // gRPC port
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
