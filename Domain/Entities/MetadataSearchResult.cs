namespace bot_kit.Domain.Entities
{
    public class MetadataSearchResult
    {
        public Guid DocumentId { get; set; }

        public string Department { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string DocumentTitle { get; set; } = string.Empty;

        public string EntityName { get; set; } = string.Empty;

        public string EntityType { get; set; } = string.Empty;

        public string RelationshipType { get; set; } = string.Empty;

        public string RelatedEntityName { get; set; } = string.Empty;
    }
}
