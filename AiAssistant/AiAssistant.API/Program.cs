using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


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

                // Keycloak puts realm roles inside "realm_access": { "roles": [...] }
                // which the JWT handler stores as a single JSON string claim.
                // Map them to ClaimTypes.Role so [Authorize(Roles = "...")] works.
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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

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
