namespace AiAssistant.API.Utils
{
    public class SqlAgentService
    {
        private readonly OllamaClient _ollama;

        public SqlAgentService(OllamaClient ollama)
        {
            _ollama = ollama;
        }

        public async Task<string> GenerateSqlAsync(string question, string schemaSummary)
        {
            var prompt = $@"
                            You are an expert SQL Server (T-SQL) query generator.

                            DATABASE SCHEMA:
                            {schemaSummary}

                            USER QUESTION:
                            {question}

                            TASK:
                            Write a SINGLE valid T-SQL SELECT query that answers the user's question.
                            Do NOT explain the query.
                            Do NOT return anything except the SQL.
                            ";

            var sql = await _ollama.GenerateAsync(prompt, "llama3");
            return sql;
        }
    }
}
