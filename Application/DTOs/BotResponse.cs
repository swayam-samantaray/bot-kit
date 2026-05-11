namespace bot_kit.Application.DTOs
{
    public class BotResponse
    {
        public string Response { get; set; } = string.Empty;

        // For RAG (very important later)
        public List<string>? Sources { get; set; }

        // Debug / Observability
        public string? ModelUsed { get; set; }

        public long? ResponseTimeMs { get; set; }
    }
}
