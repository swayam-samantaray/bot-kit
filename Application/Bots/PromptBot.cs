using bot_kit.Application.DTOs;
using bot_kit.Application.Interfaces;

namespace bot_kit.Application.Bots
{
    public class PromptBot : IBot
    {
        private readonly IOllamaService _ollamaService;

        public string BotType => "prompt";

        public PromptBot(
            IOllamaService ollamaService)
        {
            _ollamaService = ollamaService;
        }

        public async Task<BotResponse> HandleAsync(
            BotRequest request,
            CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;

            var result =
                await _ollamaService.GenerateAsync(
                    request.Prompt,
                    cancellationToken);

            return new BotResponse
            {
                Response = result,

                ModelUsed = "qwen2.5:7b",

                ResponseTimeMs =
                    (long)(DateTime.UtcNow - startTime)
                    .TotalMilliseconds
            };
        }
    }
}