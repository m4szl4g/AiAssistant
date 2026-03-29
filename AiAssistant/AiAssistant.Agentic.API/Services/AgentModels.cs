namespace AiAssistant.Agentic.API.Services;

public class AgentMessage
{
    public string Role { get; set; } = "";
    public string? Content { get; set; }
    public List<AgentToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
}

public class AgentToolCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ArgumentsJson { get; set; } = "{}";
}

public class AgentChatResponse
{
    public string FinishReason { get; set; } = "stop";
    public string? Content { get; set; }
    public List<AgentToolCall>? ToolCalls { get; set; }
}

public class AgentResponse
{
    public string Answer { get; set; } = "";
    public int Iterations { get; set; }
    public List<string> ToolsUsed { get; set; } = [];
}

public class QdrantSearchResult
{
    public string Text { get; set; } = "";
    public string DocumentName { get; set; } = "";
    public string[] Tags { get; set; } = [];
    public float Score { get; set; }
}
