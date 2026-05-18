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

        public string Department { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string SectionHeading { get; set; } = string.Empty;

        public List<string> Tags { get; set; } = new();

        public List<string> EntityNames { get; set; } = new();

        public string MetadataJson { get; set; } = "{}";

        // Chunk sequence
        public int ChunkIndex { get; set; }

        // pgvector cosine distance
        // LOWER = BETTER
        public double Distance { get; set; }
    }
}
