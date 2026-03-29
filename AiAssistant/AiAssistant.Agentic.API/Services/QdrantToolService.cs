using System.Net.Http.Json;
using System.Text.Json;
using AiAssistant.Agentic.API.Configs;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AiAssistant.Agentic.API.Services;

public class QdrantToolService
{
    private readonly HttpClient _http;
    private readonly QdrantClient _qdrant;
    private readonly string _collectionName;
    private readonly string _embedModel;
    private readonly ILogger<QdrantToolService> _logger;

    public QdrantToolService(
        HttpClient http,
        IOptions<QdrantConfig> qdrantConfig,
        IOptions<OllamaConfig> ollamaConfig,
        ILogger<QdrantToolService> logger)
    {
        _http = http;
        _qdrant = new QdrantClient(qdrantConfig.Value.Host, qdrantConfig.Value.Port);
        _collectionName = qdrantConfig.Value.CollectionName;
        _embedModel = ollamaConfig.Value.EmbedModel;
        _logger = logger;
    }

    public async Task<List<QdrantSearchResult>> SearchAsync(
        string query,
        string[]? tags,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Embedding query for Qdrant search. Query='{Query}', Tags=[{Tags}]",
            query, string.Join(", ", tags ?? []));

        var vector = await EmbedAsync(query, ct);

        Filter? filter = null;
        if (tags?.Length > 0)
        {
            filter = new Filter
            {
                Must =
                {
                    new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key = "tags",
                            Match = new Match { Keywords = new RepeatedStrings { Strings = { tags } } }
                        }
                    }
                }
            };
        }

        var results = await _qdrant.SearchAsync(_collectionName, vector, filter: filter, limit: 5);

        _logger.LogInformation("Qdrant returned {Count} results.", results.Count);

        return results.Select(r => new QdrantSearchResult
        {
            Text = r.Payload["text"].StringValue,
            DocumentName = r.Payload["document_name"].StringValue,
            Tags = r.Payload.TryGetValue("tags", out var tagsVal)
                ? tagsVal.ListValue?.Values.Select(v => v.StringValue).ToArray() ?? []
                : [],
            Score = r.Score
        }).ToList();
    }

    private async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        var payload = new { model = _embedModel, prompt = text };
        using var response = await _http.PostAsJsonAsync("/api/embeddings", payload, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("embedding").EnumerateArray()
            .Select(e => e.GetSingle())
            .ToArray();
    }
}
