namespace AiAssistant.Agentic.API.Configs;

public class OpenAiConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
    public string BaseUrl { get; set; } = "https://api.openai.com";
}
