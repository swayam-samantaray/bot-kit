using bot_kit.Domain.Entities;

namespace bot_kit.Application.Interfaces
{
    public interface IChunkingService
    {
        List<DocumentChunk> Chunk(string content, string documentName);

        List<DocumentChunk> Chunk(StructuredKnowledgeDocument document, string documentName);
    }
}
