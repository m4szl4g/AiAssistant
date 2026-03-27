using AiAssistant.API.Utils.Configs;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AiAssistant.API.Utils
{
    public class QdrantService
    {
        private readonly QdrantClient _client;
        private readonly string _collectionName;
        private const uint VectorSize = 1024; // mxbai-embed-large dimension

        public QdrantService(IOptions<QdrantConfig> config)
        {
            var cfg = config.Value;
            _client = new QdrantClient(cfg.Host, cfg.Port);
            _collectionName = cfg.CollectionName;
        }

        public async Task EnsureCollectionExistsAsync()
        {
            var collections = await _client.ListCollectionsAsync();
            if (!collections.Any(c => c == _collectionName))
            {
                await _client.CreateCollectionAsync(_collectionName, new VectorParams
                {
                    Size = VectorSize,
                    Distance = Distance.Cosine
                });
            }
        }

        public async Task UpsertChunkAsync(DocumentChunkPoint chunk)
        {
            var tagsValue = new Value { ListValue = new ListValue() };
            foreach (var tag in chunk.Tags)
                tagsValue.ListValue.Values.Add(new Value { StringValue = tag });

            var point = new PointStruct
            {
                Id = Guid.NewGuid(),
                Vectors = chunk.Vector,
                Payload =
                {
                    ["text"] = chunk.Text,
                    ["tags"] = tagsValue,
                    ["creator"] = chunk.Creator,
                    ["document_name"] = chunk.DocumentName,
                    ["chunk_index"] = (long)chunk.ChunkIndex,
                    ["created_at"] = DateTime.UtcNow.ToString("O")
                }
            };

            await _client.UpsertAsync(_collectionName, [point]);
        }

        public async Task<List<DocumentSearchResult>> SearchAsync(
            float[] queryVector,
            int topK = 5,
            string[]? tagFilter = null)
        {
            Filter? filter = null;
            if (tagFilter?.Length > 0)
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
                                Match = new Match
                                {
                                    Keywords = new RepeatedStrings { Strings = { tagFilter } }
                                }
                            }
                        }
                    }
                };
            }

            var results = await _client.SearchAsync(_collectionName, queryVector,
                filter: filter,
                limit: (ulong)topK);

            return results.Select(r => new DocumentSearchResult
            {
                Text = r.Payload["text"].StringValue,
                Tags = r.Payload.TryGetValue("tags", out var tagsVal)
                    ? tagsVal.ListValue?.Values.Select(v => v.StringValue).ToArray() ?? []
                    : [],
                Creator = r.Payload["creator"].StringValue,
                DocumentName = r.Payload["document_name"].StringValue,
                Score = r.Score
            }).ToList();
        }
    }

    public class DocumentChunkPoint
    {
        public string Text { get; set; } = "";
        public float[] Vector { get; set; } = [];
        public string[] Tags { get; set; } = [];
        public string Creator { get; set; } = "";
        public string DocumentName { get; set; } = "";
        public int ChunkIndex { get; set; }
    }

    public class DocumentSearchResult
    {
        public string Text { get; set; } = "";
        public string[] Tags { get; set; } = [];
        public string Creator { get; set; } = "";
        public string DocumentName { get; set; } = "";
        public float Score { get; set; }
    }
}
