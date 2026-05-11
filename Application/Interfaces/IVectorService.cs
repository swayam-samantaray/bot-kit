using bot_kit.Domain.Entities;

namespace bot_kit.Application.Interfaces
{
    public interface IVectorService
    {
        Task StoreAsync(List<DocumentChunk> chunks);
        Task<List<DocumentChunk>> SearchAsync(string query, int topK = 3);

        Task<List<MetadataSearchResult>> SearchMetadataAsync(
    string query);
    }
}
