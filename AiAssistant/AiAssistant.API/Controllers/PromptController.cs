using System.Text;
using AiAssistant.API.Utils;
using AiAssistant.API.Utils.Configs;
using AiAssistant.API.Utils.Queries;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AiAssistant.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PromptController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly SqlAgentService _sqlAgentService;
        private readonly OllamaClient _ollamaClient;
        private readonly QdrantService _qdrantService;
        private readonly DocumentAgentService _documentAgentService;
        private readonly ILogger<PromptController> _logger;

        public PromptController(
            IOptions<DatabaseConfig> dbSettings,
            SqlAgentService sqlAgentService,
            OllamaClient ollamaClient,
            QdrantService qdrantService,
            DocumentAgentService documentAgentService,
            ILogger<PromptController> logger)
        {
            _connectionString = dbSettings.Value.ConnectionString;
            _sqlAgentService = sqlAgentService;
            _ollamaClient = ollamaClient;
            _qdrantService = qdrantService;
            _documentAgentService = documentAgentService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<string> Query([FromBody] NaturalLanguageQuery request)
        {
            using var connection = new SqlConnection(_connectionString);
            string schemaSummary = GetSchema(connection);
            return await _sqlAgentService.GenerateSqlAsync(request.Question, schemaSummary);
        }

        /// <summary>
        /// Keress a feltöltött dokumentumokban természetes nyelvi kérdéssel.
        /// A Qdrant vektoros keresés után az Ollama modell válaszol a kontextus alapján.
        /// A találatok [TAG1][TAG2] formában tartalmazzák a dokumentum tagjeit.
        /// </summary>
        [HttpPost("document")]
        public async Task<IActionResult> SearchDocuments([FromBody] DocumentSearchQuery request)
        {
            if (request is null)
            {
                _logger.LogWarning("Document search request was null.");
                return BadRequest("Request is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Question))
            {
                _logger.LogWarning("Document search request missing question.");
                return BadRequest("Question is required.");
            }

            var tags = request.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? Array.Empty<string>();

            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["Question"] = request.Question,
                ["Tags"] = string.Join(",", tags)
            });

            try
            {
                _logger.LogInformation(
                    "Document search started. Question='{Question}', Tags=[{Tags}]",
                    request.Question,
                    string.Join(", ", tags));

                var queryVector = await _ollamaClient.EmbedAsync(request.Question);

                _logger.LogDebug("Embedding generated for document search question.");

                var results = await _qdrantService.SearchAsync(
                    queryVector,
                    topK: 7,
                    tagFilter: tags.Length > 0 ? tags : null);

                _logger.LogInformation(
                    "Qdrant search completed. ResultCount={ResultCount}",
                    results.Count);

                if (results.Count == 0)
                {
                    _logger.LogWarning(
                        "No relevant documents found for question='{Question}' with tags=[{Tags}]",
                        request.Question,
                        string.Join(", ", tags));

                    return Ok(new
                    {
                        answer = "NEM TALÁLHATÓ MEG A MEGADOTT KONTEXTUSBAN.",
                        diagnostic = new
                        {
                            reason = "No vector search results.",
                            resultCount = 0,
                            tags
                        },
                        sources = Array.Empty<object>()
                    });
                }

                var topScores = results
                    .Take(5)
                    .Select(r => new
                    {
                        r.DocumentName,
                        r.Score,
                        Tags = r.Tags
                    })
                    .ToArray();

                _logger.LogInformation(
                    "Top search results: {TopScores}",
                    System.Text.Json.JsonSerializer.Serialize(topScores));

                var ragResult = await _documentAgentService.AnswerFromContextAsync(request.Question, results);

                var sources = results.Select(r => new
                {
                    documentName = r.DocumentName,
                    tags = r.Tags,
                    tagDisplay = string.Concat(r.Tags.Select(t => $"[{t}]")),
                    creator = r.Creator,
                    score = r.Score,
                    excerpt = r.Text.Length > 200 ? r.Text[..200] + "..." : r.Text
                });

                _logger.LogInformation(
                    "Document search finished. EvidenceFound={EvidenceFound}, Reason='{Reason}', FinalAnswer='{FinalAnswer}'",
                    ragResult.EvidenceFound,
                    ragResult.Reason,
                    ragResult.Answer);

                return Ok(new
                {
                    answer = ragResult.Answer,
                    diagnostic = new
                    {
                        ragResult.EvidenceFound,
                        ragResult.Reason,
                        ragResult.UsedModel,
                        resultCount = results.Count,
                        tags
                    },
                    sources
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unhandled exception during document search. Question='{Question}', Tags=[{Tags}]",
                    request.Question,
                    string.Join(", ", tags));

                return StatusCode(500, new
                {
                    error = "Internal server error during document search."
                });
            }
        }

        private string GetSchema(SqlConnection connection)
        {
            string schemaQuery = @"
                    SELECT
                        t.TABLE_SCHEMA,
                        t.TABLE_NAME,
                        STRING_AGG(c.COLUMN_NAME + ' ' + c.DATA_TYPE, ', ') AS Columns
                    FROM INFORMATION_SCHEMA.TABLES t
                    JOIN INFORMATION_SCHEMA.COLUMNS c
                        ON t.TABLE_NAME = c.TABLE_NAME
                        AND t.TABLE_SCHEMA = c.TABLE_SCHEMA
                    WHERE t.TABLE_TYPE = 'BASE TABLE'
                    GROUP BY t.TABLE_SCHEMA, t.TABLE_NAME
                    ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME;
                ";

            var schemaElements = connection.Query(schemaQuery);
            var schemaBuilder = new StringBuilder();

            foreach (var row in schemaElements)
            {
                schemaBuilder.AppendLine($"{row.TABLE_SCHEMA}.{row.TABLE_NAME}({row.Columns})");
            }

            return schemaBuilder.ToString();
        }
    }

    public class DocumentSearchQuery
    {
        public string Question { get; set; } = "";
        public string[]? Tags { get; set; }
    }
}