using bot_kit.Application.Interfaces;
using bot_kit.Domain.Entities;

using Dapper;

using Npgsql;

using Pgvector;

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
    embedding,
    token_count
)
VALUES
(
    @DocumentId,
    @ChunkIndex,
    @ChunkContent,
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
    dc.embedding <=> @Embedding AS distance,
    d.file_name
FROM document_chunks dc
INNER JOIN documents d
    ON d.document_id = dc.document_id
ORDER BY dc.embedding <=> @Embedding
LIMIT @TopK;
";

                var vectorResults =
                    (await connection.QueryAsync(
                        vectorSql,
                        new
                        {
                            Embedding = vector,
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
    0.15 AS distance,
    d.file_name
FROM document_chunks dc
INNER JOIN documents d
    ON d.document_id = dc.document_id
WHERE LOWER(dc.chunk_content)
LIKE LOWER(@Keyword)
LIMIT @TopK;
";

                var keywordResults =
                    (await connection.QueryAsync(
                        keywordSql,
                        new
                        {
                            Keyword =
                                $"%{normalizedKeyword}%",
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

                Console.WriteLine(
                    $"Normalized Metadata Query: {normalizedQuery}");

                await using var connection =
                    await _dataSource.OpenConnectionAsync();

                // =====================================================
                // ENTITY SEARCH
                // =====================================================

                var entitySql = @"
SELECT
    e.entity_name,
    e.entity_type
FROM entities e
WHERE e.normalized_name ILIKE @Query
LIMIT 10;
";

                var entityResults =
                    await connection.QueryAsync(
                        entitySql,
                        new
                        {
                            Query =
                                $"%{normalizedQuery}%"
                        });

                foreach (var row in entityResults)
                {
                    results.Add(
                        new MetadataSearchResult
                        {
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
    source.entity_name AS source_name,
    rel.relationship_type,
    target.entity_name AS target_name
FROM entity_relationships rel
INNER JOIN entities source
    ON source.entity_id = rel.source_entity_id
INNER JOIN entities target
    ON target.entity_id = rel.target_entity_id
WHERE
    source.normalized_name ILIKE @Query
OR
    target.normalized_name ILIKE @Query
LIMIT 10;
";

                var relationshipResults =
                    await connection.QueryAsync(
                        relationshipSql,
                        new
                        {
                            Query =
                                $"%{normalizedQuery}%"
                        });

                foreach (var row in relationshipResults)
                {
                    results.Add(
                        new MetadataSearchResult
                        {
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
    }
}