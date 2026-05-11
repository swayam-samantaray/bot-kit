using bot_kit.Application.Interfaces;

using Dapper;

using Microsoft.Extensions.Options;

using Npgsql;

namespace bot_kit.Infrastructure.DocumentProcessing
{
    public class DocumentIngestionService : IDocumentIngestionService
    {
        private readonly KnowledgeBaseSettings _settings;

        private readonly DocumentParser _parser;

        private readonly IChunkingService _chunkingService;

        private readonly IVectorService _vectorService;

        private readonly IEntityExtractionService _entityExtractionService;

        private readonly NpgsqlDataSource _dataSource;

        public DocumentIngestionService(
            IOptions<KnowledgeBaseSettings> options,
            DocumentParser parser,
            IChunkingService chunkingService,
            IVectorService vectorService,
            IEntityExtractionService entityExtractionService,
            NpgsqlDataSource dataSource)
        {
            _settings = options.Value;

            _parser = parser;

            _chunkingService = chunkingService;

            _vectorService = vectorService;

            _entityExtractionService = entityExtractionService;

            _dataSource = dataSource;
        }

        public async Task IngestAsync(
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(_settings.DirectoryPath))
            {
                throw new DirectoryNotFoundException(
                    "Knowledge base folder not found.");
            }

            var files =
                Directory.GetFiles(_settings.DirectoryPath);

            await using var connection =
                await _dataSource.OpenConnectionAsync(
                    cancellationToken);

            foreach (var file in files)
            {
                Guid documentId = Guid.NewGuid();

                try
                {
                    // =====================================================
                    // PARSE DOCUMENT
                    // =====================================================

                    var content =
                        await _parser.ParseAsync(file);

                    if (string.IsNullOrWhiteSpace(content))
                    {
                        Console.WriteLine(
                            $"[SKIPPED] Empty content: {file}");

                        continue;
                    }

                    var documentName =
                        Path.GetFileName(file);

                    Console.WriteLine(
                        $"[PARSING COMPLETED] {documentName}");

                    // =====================================================
                    // REGISTER DOCUMENT
                    // =====================================================

                    var insertDocumentSql = @"
INSERT INTO documents
(
    document_id,
    file_name,
    file_type,
    raw_content,
    cleaned_content
)
VALUES
(
    @DocumentId,
    @FileName,
    @FileType,
    @RawContent,
    @CleanedContent
);";

                    await connection.ExecuteAsync(
                        insertDocumentSql,
                        new
                        {
                            DocumentId = documentId,

                            FileName = documentName,

                            FileType =
                                Path.GetExtension(file),

                            RawContent = content,

                            CleanedContent = content
                        });

                    Console.WriteLine(
                        $"[DOCUMENT REGISTERED] {documentName}");

                    // =====================================================
                    // CHUNKING
                    // =====================================================

                    var chunks =
                        _chunkingService.Chunk(
                            content,
                            documentName);

                    Console.WriteLine(
                        $"[CHUNKING COMPLETED] {documentName} → {chunks.Count} chunks");

                    // =====================================================
                    // ASSIGN DOCUMENT ID
                    // =====================================================

                    foreach (var chunk in chunks)
                    {
                        chunk.DocumentId = documentId;
                    }

                    // =====================================================
                    // STORE VECTORS
                    // =====================================================

                    await _vectorService.StoreAsync(chunks);

                    Console.WriteLine(
                        $"[VECTOR STORAGE COMPLETED] {documentName}");

                    // =====================================================
                    // ENTITY EXTRACTION
                    // =====================================================

                    await _entityExtractionService
                        .ExtractAndStoreAsync(
                            documentId,
                            content);

                    Console.WriteLine(
                        $"[ENTITY EXTRACTION COMPLETED] {documentName}");

                    // =====================================================
                    // INGESTION LOG
                    // =====================================================

                    var logSql = @"
INSERT INTO ingestion_logs
(
    log_id,
    document_id,
    status,
    message
)
VALUES
(
    @LogId,
    @DocumentId,
    @Status,
    @Message
);";

                    await connection.ExecuteAsync(
                        logSql,
                        new
                        {
                            LogId = Guid.NewGuid(),

                            DocumentId = documentId,

                            Status = "SUCCESS",

                            Message =
                                $"Successfully ingested {documentName}"
                        });

                    Console.WriteLine(
                        $"[INGESTION SUCCESS] {documentName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[ERROR] Failed processing file: {file}");

                    Console.WriteLine(ex.Message);

                    try
                    {
                        var errorLogSql = @"
INSERT INTO ingestion_logs
(
    log_id,
    document_id,
    status,
    message
)
VALUES
(
    @LogId,
    @DocumentId,
    @Status,
    @Message
);";

                        await connection.ExecuteAsync(
                            errorLogSql,
                            new
                            {
                                LogId = Guid.NewGuid(),

                                DocumentId = documentId,

                                Status = "FAILED",

                                Message = ex.Message
                            });
                    }
                    catch
                    {
                        Console.WriteLine(
                            "[ERROR] Failed writing ingestion log.");
                    }
                }
            }
        }
    }
}