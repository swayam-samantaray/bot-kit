namespace bot_kit.Domain.Entities
{
    public class MetadataSearchResult
    {
        public string EntityName { get; set; } = string.Empty;

        public string EntityType { get; set; } = string.Empty;

        public string RelationshipType { get; set; } = string.Empty;

        public string RelatedEntityName { get; set; } = string.Empty;
    }
}