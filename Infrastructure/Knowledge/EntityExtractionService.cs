using System.Text.RegularExpressions;

using bot_kit.Application.Interfaces;
using bot_kit.Domain.Entities;

using Dapper;

using Npgsql;
using System.Text.Json;

namespace bot_kit.Infrastructure.Knowledge
{
    public class EntityExtractionService
        : IEntityExtractionService
    {
        private readonly NpgsqlDataSource _dataSource;

        public EntityExtractionService(
            NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task ExtractAndStoreAsync(
            Guid documentId,
            string content)
        {
            try
            {
                var entities =
                    ExtractEntities(
                        documentId,
                        content);

                if (!entities.Any())
                {
                    Console.WriteLine(
                        "[ENTITY EXTRACTION] No entities found.");

                    return;
                }

                await using var connection =
                    await _dataSource.OpenConnectionAsync();

                // =====================================================
                // STORE ENTITIES
                // =====================================================

                var sql = @"
INSERT INTO entities
(
    entity_id,
    document_id,
    entity_name,
    entity_type,
    normalized_name
)
VALUES
(
    @Id,
    @DocumentId,
    @EntityName,
    @EntityType,
    @NormalizedName
);";

                foreach (var entity in entities)
                {
                    await connection.ExecuteAsync(
                        sql,
                        entity);

                    Console.WriteLine(
                        $"[ENTITY STORED] {entity.EntityName} ({entity.EntityType})");
                }

                // =====================================================
                // RELATIONSHIP EXTRACTION DISABLED
                // =====================================================

                Console.WriteLine(
                    "[RELATIONSHIP EXTRACTION DISABLED]");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[ENTITY EXTRACTION ERROR] {ex.Message}");
            }
        }

        public async Task StoreStructuredAsync(
            Guid documentId,
            StructuredKnowledgeDocument document)
        {
            try
            {
                await using var connection =
                    await _dataSource.OpenConnectionAsync();

                var entityMap =
                    new Dictionary<string, Guid>(
                        StringComparer.OrdinalIgnoreCase);

                var entitySql = @"
INSERT INTO entities
(
    entity_id,
    document_id,
    entity_name,
    entity_type,
    normalized_name,
    aliases,
    metadata
)
VALUES
(
    @Id,
    @DocumentId,
    @EntityName,
    @EntityType,
    @NormalizedName,
    @Aliases::jsonb,
    @Metadata::jsonb
);";

                foreach (var structuredEntity in document.Entities)
                {
                    if (string.IsNullOrWhiteSpace(structuredEntity.Name))
                    {
                        continue;
                    }

                    var entity =
                        new ExtractedEntity
                        {
                            Id = Guid.NewGuid(),
                            DocumentId = documentId,
                            EntityName = structuredEntity.Name,
                            EntityType = string.IsNullOrWhiteSpace(structuredEntity.Type)
                                ? "UNKNOWN"
                                : structuredEntity.Type.ToUpperInvariant(),
                            NormalizedName = NormalizeEntityName(structuredEntity.Name),
                            Aliases = structuredEntity.Aliases,
                            MetadataJson = JsonSerializer.Serialize(structuredEntity.Metadata)
                        };

                    await connection.ExecuteAsync(
                        entitySql,
                        new
                        {
                            entity.Id,
                            entity.DocumentId,
                            entity.EntityName,
                            entity.EntityType,
                            entity.NormalizedName,
                            Aliases = JsonSerializer.Serialize(entity.Aliases),
                            Metadata = entity.MetadataJson
                        });

                    entityMap[entity.NormalizedName] = entity.Id;

                    foreach (var alias in entity.Aliases)
                    {
                        entityMap[NormalizeEntityName(alias)] = entity.Id;
                    }

                    Console.WriteLine($"[ENTITY STORED] {entity.EntityName} ({entity.EntityType})");
                }

                await StoreRelationshipsAsync(
                    connection,
                    entityMap,
                    document.Relationships);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STRUCTURED ENTITY STORAGE ERROR] {ex.Message}");
            }
        }

        // =====================================================
        // ENTITY EXTRACTION
        // =====================================================

        private List<ExtractedEntity> ExtractEntities(
            Guid documentId,
            string content)
        {
            var entities =
                new List<ExtractedEntity>();

            // =====================================================
            // PERSON EXTRACTION
            // =====================================================

            var personRegex =
                new Regex(
                    @"\b(Mr|Ms|Mrs)\.\s+[A-Z][a-zA-Z]+\s+[A-Z][a-zA-Z]+\b");

            var people =
                personRegex.Matches(content);

            foreach (Match match in people)
            {
                var name =
                    NormalizeWhitespace(
                        match.Value.Trim());

                entities.Add(
                    new ExtractedEntity
                    {
                        DocumentId = documentId,

                        EntityName = name,

                        EntityType = "PERSON",

                        NormalizedName =
    NormalizeEntityName(name)
                    });
            }

            // =====================================================
            // ROLE EXTRACTION
            // =====================================================

            var roleRegex =
                new Regex(
                    @"\b(Head|Chief|Lead|Architect|Manager|Engineer)\s*[-:&()A-Za-z0-9 ]{2,80}",
                    RegexOptions.IgnoreCase);

            var roles =
                roleRegex.Matches(content);

            foreach (Match match in roles)
            {
                var role =
                    NormalizeWhitespace(
                        match.Value.Trim());

                // =====================================================
                // CLEAN ROLE TEXT
                // =====================================================

                role = role
                    .Replace(
                        " is being transferred",
                        "",
                        StringComparison.OrdinalIgnoreCase)

                    .Replace(
                        " and will",
                        "",
                        StringComparison.OrdinalIgnoreCase)

                    .Replace(
                        " shall continue",
                        "",
                        StringComparison.OrdinalIgnoreCase)

                    .Replace(
                        " who will",
                        "",
                        StringComparison.OrdinalIgnoreCase)

                    .Trim();

                if (role.Length < 5)
                    continue;

                if (role.Length > 100)
                    continue;

                entities.Add(
                    new ExtractedEntity
                    {
                        DocumentId = documentId,

                        EntityName = role,

                        EntityType = "ROLE",

                        NormalizedName =
                            role.ToLowerInvariant()
                    });
            }

            // =====================================================
            // ORGANIZATION EXTRACTION
            // =====================================================

            var orgRegex =
                new Regex(
                    @"\b(TPWODL|TPDDL|TPCODL|TPSODL|Tata Power|D&IT-GCC|GCC)\b",
                    RegexOptions.IgnoreCase);

            var orgs =
                orgRegex.Matches(content);

            foreach (Match match in orgs)
            {
                var org =
                    NormalizeWhitespace(
                        match.Value.Trim());

                entities.Add(
                    new ExtractedEntity
                    {
                        DocumentId = documentId,

                        EntityName = org,

                        EntityType = "ORGANIZATION",

                        NormalizedName =
                            org.ToLowerInvariant()
                    });
            }

            // =====================================================
            // POLICY EXTRACTION
            // =====================================================

            var policyRegex =
                new Regex(
                    @"\b(No employee|Only|Employees must)[^.]+\.",
                    RegexOptions.IgnoreCase);

            var policies =
                policyRegex.Matches(content);

            foreach (Match match in policies)
            {
                var policy =
                    NormalizeWhitespace(
                        match.Value.Trim());

                entities.Add(
                    new ExtractedEntity
                    {
                        DocumentId = documentId,

                        EntityName = policy,

                        EntityType = "POLICY",

                        NormalizedName =
                            policy.ToLowerInvariant()
                    });
            }

            // =====================================================
            // REMOVE DUPLICATES
            // =====================================================

            entities = entities
                .GroupBy(x =>
                    $"{x.EntityType}:{x.NormalizedName}")
                .Select(g => g.First())
                .ToList();

            Console.WriteLine(
                $"[ENTITY EXTRACTION] Total Unique Entities: {entities.Count}");

            return entities;
        }

        // =====================================================
        // HELPERS
        // =====================================================

        private string NormalizeWhitespace(
            string input)
        {
            return Regex
                .Replace(input, @"\s+", " ")
                .Trim();
        }


        private string NormalizeEntityName(string input)
        {
            input = input.ToLowerInvariant();

            input = input
                .Replace("mr.", "")
                .Replace("mr ", "")
                .Replace("ms.", "")
                .Replace("ms ", "")
                .Replace("mrs.", "")
                .Replace("mrs ", "");

            input = Regex.Replace(
                input,
                @"[^a-z0-9\s]",
                "");

            input = Regex.Replace(
                input,
                @"\s+",
                " ");

            return input.Trim();
        }

        private async Task StoreRelationshipsAsync(
            NpgsqlConnection connection,
            Dictionary<string, Guid> entityMap,
            List<StructuredRelationship> relationships)
        {
            var relationshipSql = @"
INSERT INTO entity_relationships
(
    relationship_id,
    source_entity_id,
    target_entity_id,
    relationship_type,
    confidence_score
)
VALUES
(
    @Id,
    @SourceEntityId,
    @TargetEntityId,
    @RelationshipType,
    @ConfidenceScore
);";

            foreach (var relationship in relationships)
            {
                var sourceKey =
                    NormalizeEntityName(relationship.Source);

                var targetKey =
                    NormalizeEntityName(relationship.Target);

                if (!entityMap.TryGetValue(sourceKey, out var sourceEntityId)
                    || !entityMap.TryGetValue(targetKey, out var targetEntityId))
                {
                    Console.WriteLine(
                        $"[RELATIONSHIP SKIPPED] {relationship.Source} -> {relationship.Type} -> {relationship.Target}");

                    continue;
                }

                await connection.ExecuteAsync(
                    relationshipSql,
                    new
                    {
                        Id = Guid.NewGuid(),
                        SourceEntityId = sourceEntityId,
                        TargetEntityId = targetEntityId,
                        RelationshipType = relationship.Type.ToUpperInvariant(),
                        relationship.ConfidenceScore
                    });

                Console.WriteLine(
                    $"[RELATIONSHIP STORED] {relationship.Source} -> {relationship.Type} -> {relationship.Target}");
            }
        }


    }



}
