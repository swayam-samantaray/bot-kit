namespace bot_kit.Application.Interfaces
{
    public interface IEmbeddingService
    {
        Task<List<float>> GenerateEmbeddingAsync(string text);
    }
}
