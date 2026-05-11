namespace bot_kit.Domain.Entities
{
    public class ExtractedRelationship
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SourceEntityId { get; set; }

        public Guid TargetEntityId { get; set; }

        public string RelationshipType { get; set; } = string.Empty;

        public double ConfidenceScore { get; set; }
    }
}