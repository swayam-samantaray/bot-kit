namespace bot_kit.Domain.Entities
{
    public class StructuredKnowledgeDocument
    {
        public string DocumentId { get; set; } = string.Empty;

        public string Department { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        public DateTime? EffectiveDate { get; set; }

        public List<string> Tags { get; set; } = new();

        public Dictionary<string, string> Metadata { get; set; } = new();

        public List<StructuredEntity> Entities { get; set; } = new();

        public List<StructuredRelationship> Relationships { get; set; } = new();

        public string Content { get; set; } = string.Empty;

        public List<StructuredSection> Sections { get; set; } = new();

        public string ToSearchableText()
        {
            var parts = new List<string>
            {
                $"Department: {Department}",
                $"Category: {Category}",
                $"Title: {Title}",
                $"Version: {Version}",
                $"Tags: {string.Join(", ", Tags)}"
            };

            parts.AddRange(
                Metadata.Select(x => $"{x.Key}: {x.Value}"));

            parts.AddRange(
                Entities.Select(e =>
                    $"Entity: {e.Name} Type: {e.Type} Aliases: {string.Join(", ", e.Aliases)}"));

            parts.AddRange(
                Relationships.Select(r =>
                    $"Relationship: {r.Source} {r.Type} {r.Target}"));

            if (!string.IsNullOrWhiteSpace(Content))
            {
                parts.Add(Content);
            }
            else
            {
                parts.AddRange(
                    Sections.Select(s =>
                        $"Section: {s.Heading}\n{s.Content}"));
            }

            return string.Join(
                "\n\n",
                parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
    }

    public class StructuredEntity
    {
        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public List<string> Aliases { get; set; } = new();

        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    public class StructuredRelationship
    {
        public string Source { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public string Target { get; set; } = string.Empty;

        public double ConfidenceScore { get; set; } = 1;
    }

    public class StructuredSection
    {
        public string Heading { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public List<string> Tags { get; set; } = new();

        public List<string> Entities { get; set; } = new();
    }
}
