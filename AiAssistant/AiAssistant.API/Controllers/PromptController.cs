using System.Text;
using AiAssistant.API.Utils;
using AiAssistant.API.Utils.Configs;
using AiAssistant.API.Utils.Queries;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AiAssistant.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PromptController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly SqlAgentService _sqlAgentService;

        public PromptController(IOptions<DatabaseConfig> dbSettings, SqlAgentService sqlAgentService)
        {
            _connectionString = dbSettings.Value.ConnectionString;
            _sqlAgentService = sqlAgentService;
        }

        [HttpPost]
        public async Task<string> Query([FromBody] NaturalLanguageQuery request)
        {

            using (var connection = new SqlConnection(_connectionString))
            {
                string schemaSummary = GetSchema(connection);

                string response = await _sqlAgentService.GenerateSqlAsync(
                    request.Question, schemaSummary);
                return response;
            }
        }

        private string GetSchema(SqlConnection connection)
        {
            string schemaQuery = @"
                    SELECT
                        t.TABLE_SCHEMA,
                        t.TABLE_NAME,
                        STRING_AGG(c.COLUMN_NAME + ' ' + c.DATA_TYPE, ', ') AS Columns
                    FROM INFORMATION_SCHEMA.TABLES t
                    JOIN INFORMATION_SCHEMA.COLUMNS c
                        ON t.TABLE_NAME = c.TABLE_NAME
                        AND t.TABLE_SCHEMA = c.TABLE_SCHEMA
                    WHERE t.TABLE_TYPE = 'BASE TABLE'
                    GROUP BY t.TABLE_SCHEMA, t.TABLE_NAME
                    ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME;
                ";

            var schemaElements = connection.Query(schemaQuery);
            var schemaBuilder = new StringBuilder();

            foreach (var row in schemaElements)
            {
                schemaBuilder.AppendLine($"{row.TABLE_SCHEMA}.{row.TABLE_NAME}({row.Columns})");
            }

            string schemaSummary = schemaBuilder.ToString();
            return schemaSummary;
        }
    }
}
