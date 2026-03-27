namespace AiAssistant.API.Utils
{
    public class OllamaClient
    {
        private readonly HttpClient _http;

        public OllamaClient(HttpClient http)
        {
            _http = http;
            _http.BaseAddress = new Uri("http://localhost:11434");
        }

        public async Task<string> GenerateAsync(string prompt, string model = "llama3")
        {
            var payload = new
            {
                model = model,
                prompt = prompt,
                stream = false
            };

            using var response = await _http.PostAsJsonAsync("/api/generate", payload);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>();
            return json?.Response ?? string.Empty;
        }
    }

    public class OllamaGenerateResponse
    {
        public string Model { get; set; } = "";
        public string Created_at { get; set; } = "";
        public string Response { get; set; } = "";
        public bool Done { get; set; }
    }
}
