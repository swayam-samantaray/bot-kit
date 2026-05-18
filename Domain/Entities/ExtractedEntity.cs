namespace bot_kit.Domain.Entities
{
    public class ExtractedEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid DocumentId { get; set; }

        public string EntityName { get; set; } = string.Empty;

        public string EntityType { get; set; } = string.Empty;

        public string NormalizedName { get; set; } = string.Empty;

        public List<string> Aliases { get; set; } = new();

        public string MetadataJson { get; set; } = "{}";
    }
}
