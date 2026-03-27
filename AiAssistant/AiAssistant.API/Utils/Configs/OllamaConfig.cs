namespace AiAssistant.API.Utils.Configs
{
    public class OllamaConfig
    {
        public string BaseUrl { get; set; } = "http://localhost:11434";
        public string GenerateModel { get; set; } = "llama3.2";
        public string EmbedModel { get; set; } = "nomic-embed-text";
    }
}
