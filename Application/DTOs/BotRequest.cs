namespace bot_kit.Application.DTOs
{
        public class BotRequest
        {
            public string BotType { get; set; } = string.Empty;

            public string Prompt { get; set; } = string.Empty;

            // For future use (chat history, session, etc.)
            public string? ContextId { get; set; }

            // Optional metadata (user, app, etc.)
            public Dictionary<string, string>? Metadata { get; set; }
        }

    
}
