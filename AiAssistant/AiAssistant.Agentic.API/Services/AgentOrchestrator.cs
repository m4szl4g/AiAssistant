using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AiAssistant.Agentic.API.Services;

public class AgentOrchestrator
{
    private readonly OpenAiAgentClient _openAi;
    private readonly QdrantToolService _qdrant;
    private readonly PostgresToolService _postgres;
    private readonly ILogger<AgentOrchestrator> _logger;
    private const int MaxIterations = 5;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AgentOrchestrator(
        OpenAiAgentClient openAi,
        QdrantToolService qdrant,
        PostgresToolService postgres,
        ILogger<AgentOrchestrator> logger)
    {
        _openAi = openAi;
        _qdrant = qdrant;
        _postgres = postgres;
        _logger = logger;
    }

    public async Task<AgentResponse> RunAsync(string question, CancellationToken ct = default)
    {
        var schema = await _postgres.GetSchemaAsync();

        var messages = new List<AgentMessage>
        {
            new() { Role = "system", Content = BuildSystemPrompt(schema) },
            new() { Role = "user",   Content = question }
        };

        var tools = BuildTools();
        var toolsUsed = new List<string>();
        string? rawAnswer = null;
        int usedIterations = 0;

        for (int i = 0; i < MaxIterations; i++)
        {
            usedIterations = i + 1;
            _logger.LogInformation("Agent iteration {Iteration}/{Max}. Messages={Count}",
                usedIterations, MaxIterations, messages.Count);

            var response = await _openAi.ChatWithToolsAsync(messages, tools, ct);

            messages.Add(new AgentMessage
            {
                Role = "assistant",
                Content = response.Content,
                ToolCalls = response.ToolCalls
            });

            if (response.FinishReason == "stop" || response.ToolCalls == null || response.ToolCalls.Count == 0)
            {
                rawAnswer = response.Content;
                usedIterations = i; // number of tool-calling rounds, not counting the final answer call
                _logger.LogInformation(
                    "Agent stopped tool loop after {Iterations} tool-calling iterations. ToolsUsed=[{Tools}]",
                    usedIterations, string.Join(", ", toolsUsed));
                break;
            }

            foreach (var toolCall in response.ToolCalls)
            {
                _logger.LogInformation("Tool call: {Tool} | Args: {Args}", toolCall.Name, toolCall.ArgumentsJson);
                toolsUsed.Add(toolCall.Name);

                var result = await ExecuteToolAsync(toolCall, ct);

                _logger.LogInformation("Tool result ({Tool}): {Preview}",
                    toolCall.Name, result[..Math.Min(500, result.Length)]);

                messages.Add(new AgentMessage
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Content = result
                });
            }
        }

        // Agent stopped cleanly — trust its own synthesized answer directly
        if (rawAnswer != null)
        {
            _logger.LogInformation("Agent answered cleanly. Answer='{Answer}'", rawAnswer);
            return new AgentResponse
            {
                Answer = rawAnswer,
                Iterations = usedIterations,
                ToolsUsed = toolsUsed
            };
        }

        // Agent hit max iterations without a clean stop — rationalize from tool results as fallback
        _logger.LogWarning("Agent reached max iterations ({Max}) without stopping, rationalizing.", MaxIterations);
        var finalAnswer = await RationalizeAnswerAsync(question, rawAnswer, messages, ct);

        return new AgentResponse
        {
            Answer = finalAnswer,
            Iterations = usedIterations,
            ToolsUsed = toolsUsed
        };
    }

    private async Task<string> RationalizeAnswerAsync(
        string question,
        string? candidateAnswer,
        IReadOnlyList<AgentMessage> conversationHistory,
        CancellationToken ct)
    {
        // Collect all tool results from the conversation as grounding context
        var toolResults = conversationHistory
            .Where(m => m.Role == "tool" && !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => m.Content!)
            .ToList();

        if (toolResults.Count == 0 && !string.IsNullOrWhiteSpace(candidateAnswer))
        {
            // No tools were called at all — return as-is
            return candidateAnswer!;
        }

        var context = string.Join("\n---\n", toolResults);

        var rationalizationPrompt = $"""
            Based ONLY on the retrieved information below, answer the question concisely in Hungarian.

            Important interpretation rules:
            - Company benefits (SZÉP card, cafeteria, meal voucher, transport allowance, etc.) mentioned in ANY company document apply company-wide unless explicitly stated otherwise.
            - HR policy rules (vacation entitlement, notice period, working hours) apply to all employees.
            - If the question is about a specific employee but the retrieved data contains general company policy or benefit information, use that information to answer — it applies to them too.
            - Only write NEM TALÁLHATÓ MEG A MEGADOTT KONTEXTUSBAN if the retrieved data truly contains NO relevant information at all.

            RETRIEVED DATA:
            {context}

            QUESTION: {question}

            FINAL ANSWER (Hungarian, max 3 sentences):
            """;

        _logger.LogInformation("Rationalizing final answer from {ToolResultCount} tool results.", toolResults.Count);

        var rationalizationMessages = new List<AgentMessage>
        {
            new() { Role = "user", Content = rationalizationPrompt }
        };

        // No tools available during rationalization — pure synthesis call
        var rationalizationResponse = await _openAi.ChatWithToolsAsync(rationalizationMessages, [], ct);

        var finalAnswer = rationalizationResponse.Content?.Trim();

        if (string.IsNullOrWhiteSpace(finalAnswer))
        {
            _logger.LogWarning("Rationalization returned empty answer, falling back to raw answer.");
            return candidateAnswer ?? "Nem sikerült választ generálni.";
        }

        _logger.LogInformation("Rationalized answer: '{Answer}'", finalAnswer);
        return finalAnswer;
    }

    private async Task<string> ExecuteToolAsync(AgentToolCall toolCall, CancellationToken ct)
    {
        try
        {
            return toolCall.Name switch
            {
                "search_documents" => await ExecuteSearchDocuments(toolCall.ArgumentsJson, ct),
                "execute_sql"      => await ExecuteSql(toolCall.ArgumentsJson),
                _                  => $"Unknown tool: {toolCall.Name}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed: {Tool}", toolCall.Name);
            return $"Tool error: {ex.Message}";
        }
    }

    private async Task<string> ExecuteSearchDocuments(string argsJson, CancellationToken ct)
    {
        var args = JsonSerializer.Deserialize<SearchDocumentsArgs>(argsJson, JsonOptions);
        if (args == null || string.IsNullOrWhiteSpace(args.Query))
            return "Invalid arguments for search_documents.";

        var results = await _qdrant.SearchAsync(args.Query, args.Tags, ct);

        if (results.Count == 0)
            return "No relevant documents found for this query.";

        var sb = new StringBuilder();
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"[{i + 1}] {r.DocumentName} | Tags: {string.Join(", ", r.Tags)} | Score: {r.Score:F3}");
            sb.AppendLine(r.Text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private async Task<string> ExecuteSql(string argsJson)
    {
        var args = JsonSerializer.Deserialize<ExecuteSqlArgs>(argsJson, JsonOptions);
        if (args == null || string.IsNullOrWhiteSpace(args.Sql))
            return "Invalid arguments for execute_sql.";

        return await _postgres.ExecuteSelectAsync(args.Sql);
    }

    private static string BuildSystemPrompt(string schema) => $"""
        You are an AI assistant for Képzelet Tech Solutions Kft.
        Answer questions by using the available tools. You may call multiple tools and combine results from multiple sources.

        ## Data sources — use EXACTLY the right source for each type of question:

        ### PostgreSQL database (use execute_sql):
        - Employee list, department, position, hire date → employees table
        - Actual salary amounts for specific employees → salaries table (JOIN with employees on employee_id)
        - Actual leave/vacation requests and their status → leave_requests table (JOIN with employees on employee_id)

        ### Vector document store (use search_documents):
        - HR policy documents (tags: "HR" or "ALL"): vacation entitlement, working hours, termination notice periods (felmondási idő), company policies, benefits rules
        - Employment contracts (tag: "Contract"): salary, position, probation period, SZÉP card, cafeteria, specific contract clauses
        - For benefit/perk questions (SZÉP kártya, cafeteria, egyéb juttatás): search with tag "Contract"
        - For policy questions (felmondási idő, szabadság napok, munkaidő): search with tag "HR"

        ### Database schema:
        {schema}

        ## Rules:
        1. ONLY answer questions related to employees, HR policies, contracts, salaries, benefits, or company matters. If the question is unrelated to these topics, respond ONLY with: "Csak munkavállalói és HR témájú kérdésekre tudok válaszolni." Do NOT call any tools for off-topic questions.
        2. Always use tools for relevant questions — never answer from memory or prior knowledge.
        3. For questions about a specific employee's salary: use execute_sql with a JOIN between employees and salaries.
        4. For questions about how many vacation days employees are entitled to (policy): use search_documents with tag "HR".
        5. For questions about a specific employee's actual leave requests: use execute_sql on leave_requests.
        6. A question may require BOTH sources (e.g. "salary AND vacation policy") — call both tools.
        7. STOP calling tools as soon as you have the information needed. Do NOT retry a tool that already returned data.
        8. If a tool returns results, do not call the same tool again with a similar query.
        """;

    private static JsonNode[] BuildTools() =>
    [
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = "search_documents",
                ["description"] = "Searches HR documents and employment contracts by semantic similarity. Use for: salary, vacation days, termination notice, probation period, work location, HR policies, benefits.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "The search query in Hungarian or English"
                        },
                        ["tags"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["enum"] = new JsonArray("Contract", "HR", "ALL")
                            },
                            ["description"] = "Filter by document tags. Contract = employment contracts, HR = HR policy/rules documents, ALL = all HR documents. Omit to search without tag filter."
                        }
                    },
                    ["required"] = new JsonArray("query")
                }
            }
        },
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = "execute_sql",
                ["description"] = "Executes a PostgreSQL SELECT query on the employee database. Use for: listing employees, departments, salary records, leave requests.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["sql"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "A valid PostgreSQL SELECT statement. Only SELECT is allowed."
                        }
                    },
                    ["required"] = new JsonArray("sql")
                }
            }
        }
    ];

    private record SearchDocumentsArgs(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("tags")]  string[]? Tags);

    private record ExecuteSqlArgs(
        [property: JsonPropertyName("sql")] string Sql);
}
