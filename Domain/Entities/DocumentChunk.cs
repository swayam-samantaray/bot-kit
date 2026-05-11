namespace bot_kit.Domain.Entities
{
    public class DocumentChunk
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        // PostgreSQL FK
        public Guid DocumentId { get; set; }

        // Actual chunk text
        public string Content { get; set; } = string.Empty;

        // Source document
        public string DocumentName { get; set; } = string.Empty;

        // Chunk sequence
        public int ChunkIndex { get; set; }

        // pgvector cosine distance
        // LOWER = BETTER
        public double Distance { get; set; }
    }
}
