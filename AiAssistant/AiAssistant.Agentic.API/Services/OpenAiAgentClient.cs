using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiAssistant.Agentic.API.Configs;
using Microsoft.Extensions.Options;

namespace AiAssistant.Agentic.API.Services;

public class OpenAiAgentClient
{
    private readonly HttpClient _http;
    private readonly OpenAiConfig _config;
    private readonly ILogger<OpenAiAgentClient> _logger;

    public OpenAiAgentClient(HttpClient http, IOptions<OpenAiConfig> config, ILogger<OpenAiAgentClient> logger)
    {
        _http = http;
        _config = config.Value;
        _logger = logger;
    }

    public async Task<AgentChatResponse> ChatWithToolsAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<JsonNode> tools,
        CancellationToken ct = default)
    {
        var messagesArray = new JsonArray();
        foreach (var msg in messages)
            messagesArray.Add(BuildMessageNode(msg));

        var toolsArray = new JsonArray();
        foreach (var tool in tools)
            toolsArray.Add(tool.DeepClone());

        var requestBody = new JsonObject
        {
            ["model"] = _config.Model,
            ["messages"] = messagesArray,
            ["tools"] = toolsArray,
            ["tool_choice"] = "auto"
        };

        _logger.LogDebug("Sending {MessageCount} messages to GPT, {ToolCount} tools defined.",
            messages.Count, tools.Count);

        using var content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("/v1/chat/completions", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("OpenAI error {Status}: {Body}", response.StatusCode, body);
            throw new HttpRequestException($"OpenAI returned {response.StatusCode}: {body}");
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ParseResponse(json);
    }

    private static JsonNode BuildMessageNode(AgentMessage msg)
    {
        var node = new JsonObject { ["role"] = msg.Role };

        if (msg.Role == "tool")
        {
            node["tool_call_id"] = msg.ToolCallId;
            node["content"] = msg.Content;
        }
        else if (msg.Role == "assistant" && msg.ToolCalls?.Count > 0)
        {
            node["content"] = JsonValue.Create<string?>(null);
            var toolCallsArray = new JsonArray();
            foreach (var tc in msg.ToolCalls)
            {
                toolCallsArray.Add(new JsonObject
                {
                    ["id"] = tc.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tc.Name,
                        ["arguments"] = tc.ArgumentsJson
                    }
                });
            }
            node["tool_calls"] = toolCallsArray;
        }
        else
        {
            node["content"] = msg.Content;
        }

        return node;
    }

    private static AgentChatResponse ParseResponse(JsonElement json)
    {
        var choice = json.GetProperty("choices")[0];
        var finishReason = choice.GetProperty("finish_reason").GetString() ?? "stop";
        var message = choice.GetProperty("message");

        string? content = message.TryGetProperty("content", out var contentEl)
                          && contentEl.ValueKind != JsonValueKind.Null
            ? contentEl.GetString()
            : null;

        List<AgentToolCall>? toolCalls = null;
        if (message.TryGetProperty("tool_calls", out var toolCallsEl)
            && toolCallsEl.ValueKind == JsonValueKind.Array)
        {
            toolCalls = [];
            foreach (var tc in toolCallsEl.EnumerateArray())
            {
                toolCalls.Add(new AgentToolCall
                {
                    Id = tc.GetProperty("id").GetString() ?? "",
                    Name = tc.GetProperty("function").GetProperty("name").GetString() ?? "",
                    ArgumentsJson = tc.GetProperty("function").GetProperty("arguments").GetString() ?? "{}"
                });
            }
        }

        return new AgentChatResponse
        {
            FinishReason = finishReason,
            Content = content,
            ToolCalls = toolCalls
        };
    }
}
