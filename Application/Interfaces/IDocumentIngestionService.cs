namespace bot_kit.Application.Interfaces
{
    public interface IDocumentIngestionService
    {
        Task IngestAsync(CancellationToken cancellationToken = default);
    }
}
