using AiAssistant.Agentic.API.Configs;
using AiAssistant.Agentic.API.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Config bindings
builder.Services.Configure<OpenAiConfig>(builder.Configuration.GetSection("OpenAi"));
builder.Services.Configure<PostgresConfig>(builder.Configuration.GetSection("Postgres"));
builder.Services.Configure<QdrantConfig>(builder.Configuration.GetSection("Qdrant"));
builder.Services.Configure<OllamaConfig>(builder.Configuration.GetSection("Ollama"));

// OpenAI HTTP client
builder.Services.AddHttpClient<OpenAiAgentClient>((sp, client) =>
{
    var cfg = sp.GetRequiredService<IOptions<OpenAiConfig>>().Value;
    client.BaseAddress = new Uri(cfg.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(120);
    if (!string.IsNullOrWhiteSpace(cfg.ApiKey))
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cfg.ApiKey);
});

// Ollama HTTP client (used by QdrantToolService for embeddings)
builder.Services.AddHttpClient<QdrantToolService>((sp, client) =>
{
    var cfg = sp.GetRequiredService<IOptions<OllamaConfig>>().Value;
    client.BaseAddress = new Uri(cfg.BaseUrl);
    client.Timeout = TimeSpan.FromMinutes(5);
});

// Application services
builder.Services.AddSingleton<PostgresToolService>();
builder.Services.AddScoped<AgentOrchestrator>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapControllers();

app.Run();
