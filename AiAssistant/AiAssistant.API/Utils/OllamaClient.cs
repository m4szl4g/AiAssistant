using AiAssistant.API.Utils.Configs;
using Microsoft.Extensions.Options;

namespace AiAssistant.API.Utils
{
    public class OllamaClient
    {
        private readonly HttpClient _http;
        private readonly OllamaConfig _config;

        public OllamaClient(HttpClient http, IOptions<OllamaConfig> config)
        {
            _http = http;
            _config = config.Value;
        }

        public async Task<string> GenerateAsync(string prompt, string? model = null)
        {
            var payload = new
            {
                model = model ?? _config.GenerateModel,
                prompt = prompt,
                stream = false
            };

            using var response = await _http.PostAsJsonAsync("/api/generate", payload);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>();
            return json?.Response ?? string.Empty;
        }

        public async Task<float[]> EmbedAsync(string text, string? model = null)
        {
            var payload = new
            {
                model = model ?? _config.EmbedModel,
                prompt = text
            };

            using var response = await _http.PostAsJsonAsync("/api/embeddings", payload);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>();
            return json?.Embedding ?? [];
        }
    }

    public class OllamaGenerateResponse
    {
        public string Model { get; set; } = "";
        public string Created_at { get; set; } = "";
        public string Response { get; set; } = "";
        public bool Done { get; set; }
    }

    public class OllamaEmbedResponse
    {
        public float[] Embedding { get; set; } = [];
    }
}
