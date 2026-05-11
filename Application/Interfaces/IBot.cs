using bot_kit.Application.DTOs;

namespace bot_kit.Application.Interfaces
{
    public interface IBot
    {

        string BotType { get; }
        Task<BotResponse> HandleAsync(BotRequest request, CancellationToken cancellationToken = default);

    }
}
