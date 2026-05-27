using System;

namespace Backend.Models;

public sealed class QdrantOptions
{
    public string Host { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;

    public static QdrantOptions FromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("QDRANT_HOST");
        var apiKey = Environment.GetEnvironmentVariable("QDRANT_API_KEY");

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("Thiếu biến môi trường bắt buộc: QDRANT_HOST.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Thiếu biến môi trường bắt buộc: QDRANT_API_KEY.");
        }

        return new QdrantOptions
        {
            Host = host,
            ApiKey = apiKey
        };
    }
}

