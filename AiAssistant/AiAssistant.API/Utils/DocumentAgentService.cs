using System.Text;
using AiAssistant.API.Utils.Configs;
using Microsoft.Extensions.Options;

namespace AiAssistant.API.Utils
{
    public class DocumentAgentService
    {
        private const string NoAnswerText = "NEM TALÁLHATÓ MEG A MEGADOTT KONTEXTUSBAN.";

        private readonly OllamaClient _ollama;
        private readonly OpenAiClient _openAi;
        private readonly string _model;
        private readonly ILogger<DocumentAgentService> _logger;

        public DocumentAgentService(
            OllamaClient ollama,
            OpenAiClient openAi,
            IOptions<OllamaConfig> config,
            ILogger<DocumentAgentService> logger)
        {
            _ollama = ollama;
            _openAi = openAi;
            _model = config.Value.GenerateModel;
            _logger = logger;
        }

        public async Task<DocumentAnswerResult> AnswerFromContextAsync(
            string question,
            IEnumerable<DocumentSearchResult> results,
            CancellationToken cancellationToken = default)
        {
            var resultList = results?.ToList() ?? new List<DocumentSearchResult>();

            if (string.IsNullOrWhiteSpace(question))
            {
                _logger.LogWarning("Document answer generation failed because question was empty.");

                return new DocumentAnswerResult
                {
                    Answer = NoAnswerText,
                    EvidenceFound = false,
                    Reason = "Question is required.",
                    UsedModel = _model
                };
            }

            if (resultList.Count == 0)
            {
                _logger.LogWarning("Document answer generation skipped because there were no search results.");

                return new DocumentAnswerResult
                {
                    Answer = NoAnswerText,
                    EvidenceFound = false,
                    Reason = "No vector search results.",
                    UsedModel = _model
                };
            }

            _logger.LogInformation(
                "Document answer generation started. Model={Model}, ResultCount={ResultCount}",
                _model,
                resultList.Count);

            var context = BuildContext(resultList);

            _logger.LogInformation(
                "Document context built. ContextLength={ContextLength}",
                context.Length);

            _logger.LogDebug("Document context sent to LLM:\n{Context}", context);

            var prompt = BuildPrompt(question, context);

            _logger.LogInformation(
                "Document prompt prepared. PromptLength={PromptLength}",
                prompt.Length);

            try
            {
                var ollamaTask = _ollama.GenerateAsync(prompt, _model);
                var gptTask = _openAi.ChatAsync(
                    "You are a document search assistant. Answer ONLY from the provided context. If the answer is not in the context, write only: NEM TALÁLHATÓ MEG A MEGADOTT KONTEXTUSBAN.",
                    $"CONTEXT:\n{context}\n\nQUESTION: {question}\n\nANSWER (Hungarian, one sentence max):",
                    cancellationToken);

                await Task.WhenAll((Task)ollamaTask, (Task)gptTask);

                var rawAnswer = await ollamaTask;
                var gptAnswer = await gptTask;

                _logger.LogInformation("GPT answer received. Configured={Configured}, Answer='{Answer}'",
                    _openAi.IsConfigured, gptAnswer ?? "(not configured)");

                if (string.IsNullOrWhiteSpace(rawAnswer))
                {
                    _logger.LogWarning("Model returned empty answer.");

                    return new DocumentAnswerResult
                    {
                        Answer = NoAnswerText,
                        EvidenceFound = false,
                        Reason = "Model returned empty answer.",
                        UsedModel = _model
                    };
                }

                var cleanedAnswer = rawAnswer.Trim();

                var evidenceFound = !string.Equals(
                    cleanedAnswer,
                    NoAnswerText,
                    StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation(
                    "Document answer generation completed. EvidenceFound={EvidenceFound}, AnswerLength={AnswerLength}",
                    evidenceFound,
                    cleanedAnswer.Length);

                return new DocumentAnswerResult
                {
                    Answer = cleanedAnswer,
                    GptAnswer = gptAnswer,
                    EvidenceFound = evidenceFound,
                    Reason = evidenceFound
                        ? "Answer generated from retrieved context."
                        : "Answer not found in provided context.",
                    UsedModel = _model
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unhandled exception during document answer generation.");

                return new DocumentAnswerResult
                {
                    Answer = NoAnswerText,
                    EvidenceFound = false,
                    Reason = "LLM generation failed.",
                    UsedModel = _model
                };
            }
        }

        private static string BuildContext(IEnumerable<DocumentSearchResult> results)
        {
            var sb = new StringBuilder();
            var index = 1;

            foreach (var result in results)
            {
                var tags = result.Tags ?? Enumerable.Empty<string>();
                var tagDisplay = string.Concat(tags.Select(t => $"[{t}]"));

                sb.AppendLine($"[CHUNK {index}]");
                sb.AppendLine($"Document: {result.DocumentName}");
                sb.AppendLine($"Tags: {tagDisplay}");
                sb.AppendLine("Text:");
                sb.AppendLine(result.Text ?? string.Empty);
                sb.AppendLine();

                index++;
            }

            return sb.ToString();
        }

        private static string BuildPrompt(string question, string context)
        {
            return $"""
You are a document search assistant. Answer ONLY from the CONTEXT below.
Rules:
1. If the answer is in the CONTEXT, give a short, direct answer in Hungarian.
2. If the answer is NOT in the CONTEXT, write only: NEM TALÁLHATÓ MEG A MEGADOTT KONTEXTUSBAN.
3. Do NOT add explanation. Do NOT use any knowledge outside the CONTEXT.

CONTEXT:
{context}

QUESTION: {question}

ANSWER (Hungarian, one sentence max):
""";
        }
    }

    public class DocumentAnswerResult
    {
        public string Answer { get; set; } = string.Empty;
        public string? GptAnswer { get; set; }
        public bool EvidenceFound { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string UsedModel { get; set; } = string.Empty;
    }
}