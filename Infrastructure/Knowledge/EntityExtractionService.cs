using System.Text.RegularExpressions;

using bot_kit.Application.Interfaces;
using bot_kit.Domain.Entities;

using Dapper;

using Npgsql;

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


    }



}