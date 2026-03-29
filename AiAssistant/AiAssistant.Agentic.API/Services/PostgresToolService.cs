using System.Text;
using AiAssistant.Agentic.API.Configs;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AiAssistant.Agentic.API.Services;

public class PostgresToolService
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresToolService> _logger;
    private const int MaxRows = 50;

    public PostgresToolService(IOptions<PostgresConfig> config, ILogger<PostgresToolService> logger)
    {
        _connectionString = config.Value.ConnectionString;
        _logger = logger;
    }

    public async Task<string> ExecuteSelectAsync(string sql)
    {
        if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return "Error: Only SELECT statements are allowed.";

        _logger.LogInformation("Executing SQL: {Sql}", sql);

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            var rows = (await conn.QueryAsync(sql)).Take(MaxRows).ToList();

            if (rows.Count == 0) return "No results found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Results ({rows.Count} rows):");
            foreach (var row in rows)
            {
                var dict = (IDictionary<string, object?>)row;
                sb.AppendLine(string.Join(" | ", dict.Select(kv => $"{kv.Key}: {kv.Value}")));
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL execution failed.");
            return $"SQL error: {ex.Message}";
        }
    }

    public async Task<string> GetSchemaAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            var tables = await conn.QueryAsync<(string table_name, string columns)>(@"
                SELECT table_name,
                       string_agg(column_name || ' (' || data_type || ')', ', ' ORDER BY ordinal_position) AS columns
                FROM information_schema.columns
                WHERE table_schema = 'public'
                GROUP BY table_name
                ORDER BY table_name
            ");
            return string.Join("\n", tables.Select(t => $"- {t.table_name}({t.columns})"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve DB schema.");
            return "(schema unavailable)";
        }
    }
}
