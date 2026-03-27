using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AiAssistant.API.Utils.Configs;
using Microsoft.Extensions.Options;

namespace AiAssistant.API.Utils
{
    public class OpenAiClient
    {
        private readonly HttpClient _http;
        private readonly OpenAiConfig _config;
        private readonly ILogger<OpenAiClient> _logger;

        public OpenAiClient(HttpClient http, IOptions<OpenAiConfig> config, ILogger<OpenAiClient> logger)
        {
            _http = http;
            _config = config.Value;
            _logger = logger;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_config.ApiKey);

        public async Task<string?> ChatAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                return null;

            var payload = new
            {
                model = _config.Model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userMessage }
                }
            };

            try
            {
                using var response = await _http.PostAsJsonAsync("/v1/chat/completions", payload, cancellationToken);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: cancellationToken);
                return result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI chat request failed. Model={Model}", _config.Model);
                return null;
            }
        }
    }

    public class OpenAiChatResponse
    {
        [JsonPropertyName("choices")]
        public List<OpenAiChoice> Choices { get; set; } = [];
    }

    public class OpenAiChoice
    {
        [JsonPropertyName("message")]
        public OpenAiMessage Message { get; set; } = new();
    }

    public class OpenAiMessage
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}
