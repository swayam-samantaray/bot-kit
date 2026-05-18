using bot_kit.Application.Interfaces;
using bot_kit.Domain.Entities;

using Dapper;

using Npgsql;

using Pgvector;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace bot_kit.Infrastructure.VectorDB
{
    public class PostgresVectorService : IVectorService
    {
        private readonly NpgsqlDataSource _dataSource;

        private readonly IEmbeddingService _embeddingService;

        public PostgresVectorService(
            NpgsqlDataSource dataSource,
            IEmbeddingService embeddingService)
        {
            _dataSource = dataSource;

            _embeddingService = embeddingService;
        }

        // =========================================================
        // STORE DOCUMENT CHUNKS
        // =========================================================

        public async Task StoreAsync(List<DocumentChunk> chunks)
        {
            await using var connection =
                await _dataSource.OpenConnectionAsync();

            foreach (var chunk in chunks)
            {
                try
                {
                    // =====================================================
                    // GENERATE EMBEDDING
                    // =====================================================

                    var embedding =
                        await _embeddingService
                            .GenerateEmbeddingAsync(
                                chunk.Content);

                    Console.WriteLine(
                        $"Embedding Size: {embedding.Count}");

                    // =====================================================
                    // CONVERT TO PGVECTOR
                    // =====================================================

                    var vector =
                        new Vector(
                            embedding
                                .Select(x => (float)x)
                                .ToArray());

                    // =====================================================
                    // INSERT CHUNK
                    // =====================================================

                    var sql = @"
INSERT INTO document_chunks
(
    document_id,
    chunk_index,
    chunk_content,
    department,
    category,
    title,
    section_heading,
    tags,
    entity_names,
    metadata,
    embedding,
    token_count
)
VALUES
(
    @DocumentId,
    @ChunkIndex,
    @ChunkContent,
    @Department,
    @Category,
    @Title,
    @SectionHeading,
    @Tags,
    @EntityNames,
    @Metadata::jsonb,
    @Embedding,
    @TokenCount
);";

                    await connection.ExecuteAsync(
                        sql,
                        new
                        {
                            DocumentId = chunk.DocumentId,

                            ChunkIndex = chunk.ChunkIndex,

                            ChunkContent = chunk.Content,

                            chunk.Department,

                            chunk.Category,

                            chunk.Title,

                            chunk.SectionHeading,

                            Tags =
                                chunk.Tags.ToArray(),

                            EntityNames =
                                chunk.EntityNames.ToArray(),

                            Metadata =
                                string.IsNullOrWhiteSpace(chunk.MetadataJson)
                                    ? "{}"
                                    : chunk.MetadataJson,

                            Embedding = vector,

                            TokenCount =
                                chunk.Content
                                    .Split(
                                        ' ',
                                        StringSplitOptions.RemoveEmptyEntries)
                                    .Length
                        });

                    Console.WriteLine(
                        $"[STORED] Chunk {chunk.ChunkIndex} from {chunk.DocumentName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[ERROR] Failed storing chunk: {ex.Message}");
                }
            }
        }

        // =========================================================
        // HYBRID SEARCH
        // =========================================================

        public async Task<List<DocumentChunk>> SearchAsync(
            string query,
            int topK = 5)
        {
            var finalResults =
                new List<DocumentChunk>();

            try
            {
                // =====================================================
                // NORMALIZE QUERY
                // =====================================================

                var normalizedKeyword =
                    NormalizeQuery(query);

                var searchTerms =
                    BuildSearchTerms(normalizedKeyword);

                Console.WriteLine(
                    $"Original Query: {query}");

                Console.WriteLine(
                    $"Normalized Query: {normalizedKeyword}");

                // =====================================================
                // GENERATE QUERY EMBEDDING
                // =====================================================

                var embedding =
                    await _embeddingService
                        .GenerateEmbeddingAsync(query);

                Console.WriteLine(
                    $"Embedding Dimension: {embedding.Count}");

                var vector =
                    new Vector(
                        embedding
                            .Select(x => (float)x)
                            .ToArray());

                await using var connection =
                    await _dataSource.OpenConnectionAsync();

                // =====================================================
                // VECTOR SEARCH
                // =====================================================

                var vectorSql = @"
SELECT
    dc.chunk_id,
    dc.document_id,
    dc.chunk_index,
    dc.chunk_content,
    dc.department,
    dc.category,
    dc.title,
    dc.section_heading,
    dc.tags,
    dc.entity_names,
    (
        (dc.embedding <=> @Embedding)
        - CASE
            WHEN LOWER(COALESCE(dc.department, '')) LIKE LOWER(@Keyword) THEN 0.12::double precision
            WHEN LOWER(COALESCE(dc.title, '')) LIKE LOWER(@Keyword) THEN 0.10::double precision
            WHEN LOWER(COALESCE(dc.category, '')) LIKE LOWER(@Keyword) THEN 0.08::double precision
            WHEN LOWER(COALESCE(dc.section_heading, '')) LIKE LOWER(@Keyword) THEN 0.08::double precision
            WHEN LOWER(COALESCE(array_to_string(dc.tags, ' '), '')) LIKE LOWER(@Keyword) THEN 0.08::double precision
            WHEN LOWER(COALESCE(array_to_string(dc.entity_names, ' '), '')) LIKE LOWER(@Keyword) THEN 0.08::double precision
            WHEN EXISTS (
                SELECT 1
                FROM unnest(@SearchTerms::text[]) AS term
                WHERE LOWER(
                    CONCAT_WS(
                        ' ',
                        dc.department,
                        dc.category,
                        dc.title,
                        dc.section_heading,
                        array_to_string(dc.tags, ' '),
                        array_to_string(dc.entity_names, ' ')
                    )
                ) LIKE '%' || LOWER(term) || '%'
            ) THEN 0.06::double precision
            ELSE 0::double precision
          END
    ) AS distance,
    d.file_name
FROM document_chunks dc
INNER JOIN documents d
    ON d.document_id = dc.document_id
ORDER BY distance
LIMIT @TopK;
";

                var vectorResults =
                    (await connection.QueryAsync(
                        vectorSql,
                        new
                        {
                            Embedding = vector,
                            Keyword = $"%{normalizedKeyword}%",
                            SearchTerms = searchTerms,
                            TopK = topK
                        }))
                    .ToList();

                Console.WriteLine(
                    $"Vector Results Count: {vectorResults.Count}");

                // =====================================================
                // KEYWORD SEARCH
                // =====================================================

                var keywordSql = @"
SELECT
    dc.chunk_id,
    dc.document_id,
    dc.chunk_index,
    dc.chunk_content,
    dc.department,
    dc.category,
    dc.title,
    dc.section_heading,
    dc.tags,
    dc.entity_names,
    0.15 AS distance,
    d.file_name
FROM document_chunks dc
INNER JOIN documents d
    ON d.document_id = dc.document_id
WHERE
    LOWER(dc.chunk_content) LIKE LOWER(@Keyword)
OR
    LOWER(COALESCE(dc.department, '')) LIKE LOWER(@Keyword)
OR
    LOWER(COALESCE(dc.category, '')) LIKE LOWER(@Keyword)
OR
    LOWER(COALESCE(dc.title, '')) LIKE LOWER(@Keyword)
OR
    LOWER(COALESCE(dc.section_heading, '')) LIKE LOWER(@Keyword)
OR
    LOWER(COALESCE(array_to_string(dc.tags, ' '), '')) LIKE LOWER(@Keyword)
OR
    LOWER(COALESCE(array_to_string(dc.entity_names, ' '), '')) LIKE LOWER(@Keyword)
OR
    EXISTS (
        SELECT 1
        FROM unnest(@SearchTerms::text[]) AS term
        WHERE LOWER(
            CONCAT_WS(
                ' ',
                dc.chunk_content,
                dc.department,
                dc.category,
                dc.title,
                dc.section_heading,
                array_to_string(dc.tags, ' '),
                array_to_string(dc.entity_names, ' ')
            )
        ) LIKE '%' || LOWER(term) || '%'
    )
LIMIT @TopK;
";

                var keywordResults =
                    (await connection.QueryAsync(
                        keywordSql,
                        new
                        {
                            Keyword =
                                $"%{normalizedKeyword}%",
                            SearchTerms = searchTerms,
                            TopK = topK
                        }))
                    .ToList();

                Console.WriteLine(
                    $"Keyword Results Count: {keywordResults.Count}");

                // =====================================================
                // MERGE RESULTS
                // =====================================================

                var merged =
                    vectorResults
                        .Concat(keywordResults)
                        .GroupBy(x => (Guid)x.chunk_id)
                        .Select(g => g.First())
                        .OrderBy(x => (double)x.distance)
                        .Take(topK)
                        .ToList();

                Console.WriteLine(
                    $"Merged Results Count: {merged.Count}");

                // =====================================================
                // MAP RESULTS
                // =====================================================

                foreach (var row in merged)
                {
                    finalResults.Add(new DocumentChunk
                    {
                        Id =
                            row.chunk_id.ToString(),

                        DocumentId =
                            row.document_id,

                        ChunkIndex =
                            row.chunk_index,

                        Content =
                            row.chunk_content,

                        DocumentName =
                            row.file_name,

                        Department =
                            row.department ?? string.Empty,

                        Category =
                            row.category ?? string.Empty,

                        Title =
                            row.title ?? string.Empty,

                        SectionHeading =
                            row.section_heading ?? string.Empty,

                        Tags =
                            ReadStringList(row.tags),

                        EntityNames =
                            ReadStringList(row.entity_names),

                        Distance =
                            (double)row.distance
                    });
                }

                // =====================================================
                // DEBUG LOGGING
                // =====================================================

                Console.WriteLine(
                    "=== HYBRID SEARCH RESULTS ===");

                foreach (var item in finalResults)
                {
                    Console.WriteLine(
                        $"Distance: {item.Distance}");

                    Console.WriteLine(
                        $"Doc: {item.DocumentName}");

                    Console.WriteLine(item.Content);

                    Console.WriteLine("--------------------------------");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[ERROR] Hybrid search failed: {ex.Message}");
            }

            return finalResults;
        }

        // =========================================================
        // METADATA SEARCH
        // =========================================================

        public async Task<List<MetadataSearchResult>>
            SearchMetadataAsync(string query)
        {
            var results =
                new List<MetadataSearchResult>();

            try
            {
                var normalizedQuery =
                    NormalizeQuery(query)
                        .ToLowerInvariant();

                var searchTerms =
                    BuildSearchTerms(normalizedQuery);

                Console.WriteLine(
                    $"Normalized Metadata Query: {normalizedQuery}");

                await using var connection =
                    await _dataSource.OpenConnectionAsync();

                // =====================================================
                // ENTITY SEARCH
                // =====================================================

                var entitySql = @"
SELECT
    e.document_id,
    d.department,
    d.category,
    d.title,
    e.entity_name,
    e.entity_type
FROM entities e
INNER JOIN documents d
    ON d.document_id = e.document_id
WHERE e.normalized_name ILIKE @Query
OR LOWER(COALESCE(e.aliases::text, '')) LIKE LOWER(@Query)
OR LOWER(COALESCE(d.department, '')) LIKE LOWER(@Query)
OR LOWER(COALESCE(d.category, '')) LIKE LOWER(@Query)
OR LOWER(COALESCE(d.title, '')) LIKE LOWER(@Query)
OR LOWER(COALESCE(d.tags::text, '')) LIKE LOWER(@Query)
OR EXISTS (
    SELECT 1
    FROM unnest(@SearchTerms::text[]) AS term
    WHERE LOWER(
        CONCAT_WS(
            ' ',
            e.entity_name,
            e.entity_type,
            e.aliases::text,
            d.department,
            d.category,
            d.title,
            d.tags::text
        )
    ) LIKE '%' || LOWER(term) || '%'
)
LIMIT 10;
";

                var entityResults =
                    await connection.QueryAsync(
                        entitySql,
                        new
                        {
                            Query =
                                $"%{normalizedQuery}%",
                            SearchTerms = searchTerms
                        });

                foreach (var row in entityResults)
                {
                    results.Add(
                        new MetadataSearchResult
                        {
                            DocumentId =
                                row.document_id,

                            Department =
                                row.department ?? string.Empty,

                            Category =
                                row.category ?? string.Empty,

                            DocumentTitle =
                                row.title ?? string.Empty,

                            EntityName =
                                row.entity_name,

                            EntityType =
                                row.entity_type
                        });
                }

                Console.WriteLine(
                    $"Metadata Entity Results: {results.Count}");

                // =====================================================
                // RELATIONSHIP SEARCH
                // =====================================================

                var relationshipSql = @"
SELECT
    source.document_id,
    d.department,
    d.category,
    d.title,
    source.entity_name AS source_name,
    rel.relationship_type,
    target.entity_name AS target_name
FROM entity_relationships rel
INNER JOIN entities source
    ON source.entity_id = rel.source_entity_id
INNER JOIN entities target
    ON target.entity_id = rel.target_entity_id
INNER JOIN documents d
    ON d.document_id = source.document_id
WHERE
    source.normalized_name ILIKE @Query
OR
    target.normalized_name ILIKE @Query
OR
    LOWER(COALESCE(source.aliases::text, '')) LIKE LOWER(@Query)
OR
    LOWER(COALESCE(target.aliases::text, '')) LIKE LOWER(@Query)
OR
    EXISTS (
        SELECT 1
        FROM unnest(@SearchTerms::text[]) AS term
        WHERE LOWER(
            CONCAT_WS(
                ' ',
                source.entity_name,
                source.aliases::text,
                target.entity_name,
                target.aliases::text,
                rel.relationship_type,
                d.department,
                d.category,
                d.title
            )
        ) LIKE '%' || LOWER(term) || '%'
    )
LIMIT 10;
";

                var relationshipResults =
                    await connection.QueryAsync(
                        relationshipSql,
                        new
                        {
                            Query =
                                $"%{normalizedQuery}%",
                            SearchTerms = searchTerms
                        });

                foreach (var row in relationshipResults)
                {
                    results.Add(
                        new MetadataSearchResult
                        {
                            DocumentId =
                                row.document_id,

                            Department =
                                row.department ?? string.Empty,

                            Category =
                                row.category ?? string.Empty,

                            DocumentTitle =
                                row.title ?? string.Empty,

                            EntityName =
                                row.source_name,

                            RelationshipType =
                                row.relationship_type,

                            RelatedEntityName =
                                row.target_name
                        });
                }

                Console.WriteLine(
                    $"Total Metadata Results: {results.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[METADATA SEARCH ERROR] {ex.Message}");
            }

            return results;
        }

        // =========================================================
        // QUERY NORMALIZATION
        // =========================================================

        private string NormalizeQuery(string query)
        {
            query = query.ToLowerInvariant();

            // ============================================
            // REMOVE COMMON QUESTION PHRASES
            // ============================================

            var stopPhrases = new[]
            {
        "do you have any information about",
        "tell me about",
        "who is",
        "what is",
        "can you tell me about",
        "give me information about",
        "give information about",
        "information about",
        "details about",
        "who reports to",
        "do you know about"
    };

            foreach (var phrase in stopPhrases)
            {
                query = query.Replace(
                    phrase,
                    "",
                    StringComparison.OrdinalIgnoreCase);
            }

            // ============================================
            // REMOVE PREFIXES
            // ============================================

            query = query
                .Replace("mr.", "")
                .Replace("mr ", "")
                .Replace("ms.", "")
                .Replace("ms ", "")
                .Replace("mrs.", "")
                .Replace("mrs ", "");

            // ============================================
            // REMOVE SYMBOLS
            // ============================================

            query = System.Text.RegularExpressions.Regex.Replace(
                query,
                @"[^a-z0-9\s]",
                " ");

            // ============================================
            // NORMALIZE WHITESPACE
            // ============================================

            query = System.Text.RegularExpressions.Regex.Replace(
                query,
                @"\s+",
                " ");

            return query.Trim();
        }

        private string[] BuildSearchTerms(string normalizedQuery)
        {
            var stopWords =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "a",
                    "an",
                    "and",
                    "are",
                    "can",
                    "do",
                    "for",
                    "how",
                    "i",
                    "in",
                    "is",
                    "of",
                    "on",
                    "please",
                    "take",
                    "tell",
                    "the",
                    "to",
                    "what",
                    "when",
                    "where",
                    "who"
                };

            var terms =
                Regex.Split(normalizedQuery, @"\s+")
                    .Where(x => x.Length > 2)
                    .Where(x => !stopWords.Contains(x))
                    .Select(Singularize)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (!terms.Any() && !string.IsNullOrWhiteSpace(normalizedQuery))
            {
                terms.Add(normalizedQuery);
            }

            return terms.ToArray();
        }

        private string Singularize(string term)
        {
            if (term.EndsWith("ies", StringComparison.OrdinalIgnoreCase)
                && term.Length > 4)
            {
                return term[..^3] + "y";
            }

            if (term.EndsWith("es", StringComparison.OrdinalIgnoreCase)
                && term.Length > 4)
            {
                return term[..^2];
            }

            if (term.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                && term.Length > 3)
            {
                return term[..^1];
            }

            return term;
        }

        private List<string> ReadStringList(dynamic value)
        {
            if (value == null)
            {
                return new List<string>();
            }

            if (value is string[] array)
            {
                return array.ToList();
            }

            if (value is IEnumerable<string> strings)
            {
                return strings.ToList();
            }

            if (value is string text)
            {
                try
                {
                    return JsonSerializer.Deserialize<List<string>>(text)
                        ?? new List<string>();
                }
                catch
                {
                    return new List<string> { text };
                }
            }

            return new List<string>();
        }
    }
}
