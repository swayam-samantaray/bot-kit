namespace bot_kit.Application.Interfaces
{
    public interface IOllamaService
    {
        Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default);
    }
}
