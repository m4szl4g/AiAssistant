namespace AiAssistant.API.Utils.Configs
{
    public class QdrantConfig
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 6334;
        public string CollectionName { get; set; } = "documents";
    }
}
