using System.Security.Claims;
using System.Text.Json;
using AiAssistant.API.Utils;
using AiAssistant.API.Utils.Configs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Config bindings
builder.Services.Configure<DatabaseConfig>(builder.Configuration.GetSection("Database"));
builder.Services.Configure<OllamaConfig>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<QdrantConfig>(builder.Configuration.GetSection("Qdrant"));
builder.Services.Configure<OpenAiConfig>(builder.Configuration.GetSection("OpenAi"));

// OpenAI HTTP client
builder.Services.AddHttpClient<OpenAiClient>((sp, client) =>
{
    var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAiConfig>>().Value;
    client.BaseAddress = new Uri(cfg.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
    if (!string.IsNullOrWhiteSpace(cfg.ApiKey))
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cfg.ApiKey);
});

// Ollama HTTP client — base URL from config
builder.Services.AddHttpClient<OllamaClient>((sp, client) =>
{
    var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaConfig>>().Value;
    client.BaseAddress = new Uri(cfg.BaseUrl);
    client.Timeout = TimeSpan.FromMinutes(10);
});

// Application services
builder.Services.AddSingleton<QdrantService>();
builder.Services.AddScoped<SqlAgentService>();
builder.Services.AddScoped<DocumentAgentService>();
builder.Services.AddSingleton<PdfChunker>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Keycloak:Authority"];
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = BuildValidIssuers(builder.Configuration),
            ValidateAudience = false,
            ValidateLifetime = true,
            RoleClaimType = ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is not ClaimsIdentity identity)
                    return Task.CompletedTask;

                var realmAccessClaim = identity.FindFirst("realm_access");
                if (realmAccessClaim is null)
                    return Task.CompletedTask;

                try
                {
                    var realmAccess = JsonSerializer.Deserialize<JsonElement>(realmAccessClaim.Value);
                    if (realmAccess.TryGetProperty("roles", out var roles))
                    {
                        foreach (var role in roles.EnumerateArray())
                        {
                            var roleStr = role.GetString();
                            if (!string.IsNullOrEmpty(roleStr) && !identity.HasClaim(ClaimTypes.Role, roleStr))
                                identity.AddClaim(new Claim(ClaimTypes.Role, roleStr));
                        }
                    }
                }
                catch (JsonException ex)
                {
                    Console.WriteLine(ex.Message, "Failed to parse 'realm_access' claim from JWT token.");
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

static IEnumerable<string> BuildValidIssuers(IConfiguration configuration)
{
    var authority = configuration["Keycloak:Authority"]!;
    yield return authority;
    var tokenIssuer = configuration["Keycloak:TokenIssuer"];
    if (!string.IsNullOrEmpty(tokenIssuer) && tokenIssuer != authority)
        yield return tokenIssuer;
}
