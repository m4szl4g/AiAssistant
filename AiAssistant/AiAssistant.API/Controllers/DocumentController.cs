using System.Diagnostics;
using System.Security.Claims;
using AiAssistant.API.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiAssistant.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DocumentController : ControllerBase
    {
        private readonly PdfChunker _pdfChunker;
        private readonly OllamaClient _ollamaClient;
        private readonly QdrantService _qdrantService;
        private readonly ILogger<DocumentController> _logger;

        public DocumentController(
            PdfChunker pdfChunker,
            OllamaClient ollamaClient,
            QdrantService qdrantService,
            ILogger<DocumentController> logger)
        {
            _pdfChunker = pdfChunker;
            _ollamaClient = ollamaClient;
            _qdrantService = qdrantService;
            _logger = logger;
        }

        /// <summary>
        /// HR uploads a PDF document. The file is chunked, embedded and stored in Qdrant.
        /// Tags are stored as [TAG1][TAG2] metadata alongside each chunk.
        /// </summary>
        [HttpPost("upload")]
        [Authorize(Roles = "hr")]
        public async Task<IActionResult> Upload(
            [FromForm] IFormFile file,
            [FromForm] string? tags)
        {
            var overallStopwatch = Stopwatch.StartNew();

            var creator = User.FindFirst("preferred_username")?.Value
                       ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? "unknown";

            _logger.LogInformation(
                "Document upload started. Creator: {Creator}, FileName: {FileName}, FileLength: {FileLength}, RawTags: {RawTags}",
                creator,
                file?.FileName,
                file?.Length,
                tags);

            if (file == null || file.Length == 0)
            {
                _logger.LogWarning(
                    "Document upload rejected because no file was provided. Creator: {Creator}",
                    creator);

                return BadRequest("No file provided.");
            }

            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Document upload rejected because file is not a PDF. Creator: {Creator}, FileName: {FileName}",
                    creator,
                    file.FileName);

                return BadRequest("Only PDF files are accepted.");
            }

            string[] tagList;
            try
            {
                tagList = ParseTags(tags);

                _logger.LogInformation(
                    "Tags parsed successfully. Creator: {Creator}, FileName: {FileName}, Tags: {Tags}",
                    creator,
                    file.FileName,
                    string.Join(", ", tagList));
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to parse tags. Creator: {Creator}, FileName: {FileName}, RawTags: {RawTags}",
                    creator,
                    file.FileName,
                    tags);

                return BadRequest("Invalid tags format.");
            }

            try
            {
                _logger.LogInformation(
                    "Ensuring Qdrant collection exists. Creator: {Creator}, FileName: {FileName}",
                    creator,
                    file.FileName);

                await _qdrantService.EnsureCollectionExistsAsync();

                _logger.LogInformation(
                    "Qdrant collection verified. Creator: {Creator}, FileName: {FileName}",
                    creator,
                    file.FileName);

                List<string> chunks;
                var chunkingStopwatch = Stopwatch.StartNew();

                using (var stream = file.OpenReadStream())
                {
                    _logger.LogInformation(
                        "Starting PDF chunking. Creator: {Creator}, FileName: {FileName}",
                        creator,
                        file.FileName);

                    chunks = _pdfChunker.Chunk(stream);
                }

                chunkingStopwatch.Stop();

                _logger.LogInformation(
                    "PDF chunking completed. Creator: {Creator}, FileName: {FileName}, ChunkCount: {ChunkCount}, ChunkingDurationMs: {ChunkingDurationMs}",
                    creator,
                    file.FileName,
                    chunks.Count,
                    chunkingStopwatch.ElapsedMilliseconds);

                if (chunks.Count == 0)
                {
                    _logger.LogWarning(
                        "No text could be extracted from PDF. Creator: {Creator}, FileName: {FileName}",
                        creator,
                        file.FileName);

                    return BadRequest("Could not extract text from the PDF.");
                }

                for (int i = 0; i < chunks.Count; i++)
                {
                    var chunkStopwatch = Stopwatch.StartNew();

                    _logger.LogInformation(
                        "Processing chunk started. Creator: {Creator}, FileName: {FileName}, ChunkIndex: {ChunkIndex}, ChunkLength: {ChunkLength}",
                        creator,
                        file.FileName,
                        i,
                        chunks[i]?.Length ?? 0);

                    float[] embedding;
                    try
                    {
                        embedding = await _ollamaClient.EmbedAsync(chunks[i]);

                        _logger.LogInformation(
                            "Embedding created successfully. Creator: {Creator}, FileName: {FileName}, ChunkIndex: {ChunkIndex}, VectorSize: {VectorSize}",
                            creator,
                            file.FileName,
                            i,
                            embedding?.Length ?? 0);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Embedding failed. Creator: {Creator}, FileName: {FileName}, ChunkIndex: {ChunkIndex}",
                            creator,
                            file.FileName,
                            i);

                        throw;
                    }

                    try
                    {
                        await _qdrantService.UpsertChunkAsync(new DocumentChunkPoint
                        {
                            Text = chunks[i],
                            Vector = embedding ?? [],
                            Tags = tagList,
                            Creator = creator,
                            DocumentName = file.FileName,
                            ChunkIndex = i
                        });

                        chunkStopwatch.Stop();

                        _logger.LogInformation(
                            "Chunk stored successfully in Qdrant. Creator: {Creator}, FileName: {FileName}, ChunkIndex: {ChunkIndex}, DurationMs: {DurationMs}",
                            creator,
                            file.FileName,
                            i,
                            chunkStopwatch.ElapsedMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Qdrant upsert failed. Creator: {Creator}, FileName: {FileName}, ChunkIndex: {ChunkIndex}",
                            creator,
                            file.FileName,
                            i);

                        throw;
                    }
                }

                overallStopwatch.Stop();

                _logger.LogInformation(
                    "Document upload completed successfully. Creator: {Creator}, FileName: {FileName}, ChunkCount: {ChunkCount}, TotalDurationMs: {TotalDurationMs}",
                    creator,
                    file.FileName,
                    chunks.Count,
                    overallStopwatch.ElapsedMilliseconds);

                return Ok(new
                {
                    documentName = file.FileName,
                    chunksStored = chunks.Count,
                    tags = tagList,
                    creator
                });
            }
            catch (Exception ex)
            {
                overallStopwatch.Stop();

                _logger.LogError(
                    ex,
                    "Document upload failed. Creator: {Creator}, FileName: {FileName}, TotalDurationMs: {TotalDurationMs}",
                    creator,
                    file?.FileName,
                    overallStopwatch.ElapsedMilliseconds);

                return StatusCode(StatusCodes.Status500InternalServerError,
                    "An error occurred while processing the document.");
            }
        }

        /// <summary>
        /// HR deletes all chunks belonging to a document by its file name.
        /// </summary>
        [HttpDelete]
        [Authorize(Roles = "hr")]
        public async Task<IActionResult> Delete([FromQuery] string documentName)
        {
            if (string.IsNullOrWhiteSpace(documentName))
                return BadRequest("documentName is required.");

            var result = await _qdrantService.DeleteByDocumentNameAsync(documentName);

            _logger.LogInformation(
                "Document deleted. DocumentName={DocumentName}, Status={Status}",
                documentName, result.Status);

            return Ok(new { documentName, status = result.Status.ToString() });
        }

        private string[] ParseTags(string? tags)
        {
            if (string.IsNullOrWhiteSpace(tags))
                return [];

            // Accept JSON array ["tag1","tag2"] or comma-separated tag1,tag2
            tags = tags.Trim();
            if (tags.StartsWith('['))
            {
                try
                {
                    return System.Text.Json.JsonSerializer.Deserialize<string[]>(tags) ?? [];
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "failed to parse tags");
                }
            }

            return tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Select(t => t.Trim('[', ']'))
                       .Where(t => !string.IsNullOrWhiteSpace(t))
                       .ToArray();
        }
    }
}