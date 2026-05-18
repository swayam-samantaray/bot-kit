using bot_kit.Domain.Entities;

namespace bot_kit.Application.Interfaces
{
    public interface IEntityExtractionService
    {
        Task ExtractAndStoreAsync(
            Guid documentId,
            string content);

        Task StoreStructuredAsync(
            Guid documentId,
            StructuredKnowledgeDocument document);
    }
}
