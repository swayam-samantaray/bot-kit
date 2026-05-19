using bot_kit.Application.Interfaces;
using bot_kit.Domain.Entities;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Text.Json;

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
                SelectFilesForIngestion(
                    Directory.GetFiles(
                    _settings.DirectoryPath,
                    "*.*",
                    SearchOption.AllDirectories));

            await using var connection =
                await _dataSource.OpenConnectionAsync(
                    cancellationToken);

            foreach (var file in files)
            {
                Guid documentId = Guid.Empty;

                try
                {
                    var documentName =
                        Path.GetFileName(file);

                    var fallbackDepartment =
                        GetDepartmentFromPath(file);

                    var extension =
                        Path.GetExtension(file).ToLowerInvariant();

                    StructuredKnowledgeDocument? structuredDocument = null;
                    string content;

                    if (IsPreparedKnowledgeJson(file))
                    {
                        structuredDocument =
                            await _parser.ParseStructuredJsonAsync(
                                file,
                                fallbackDepartment);

                        if (structuredDocument == null)
                        {
                            Console.WriteLine($"[SKIPPED] Invalid JSON document: {file}");
                            continue;
                        }

                        content = structuredDocument.ToSearchableText();
                    }
                    else
                    {
                        content =
                            await _parser.ParseAsync(file);
                    }

                    if (string.IsNullOrWhiteSpace(content))
                    {
                        Console.WriteLine($"[SKIPPED] Empty content: {file}");
                        continue;
                    }

                    Console.WriteLine($"[PARSING COMPLETED] {documentName}");

                    documentId =
                        await RegisterDocumentAsync(
                            connection,
                            file,
                            documentName,
                            content,
                            structuredDocument,
                            fallbackDepartment);

                    await ClearDocumentKnowledgeAsync(
                        connection,
                        documentId);

                    Console.WriteLine($"[DOCUMENT REGISTERED] {documentName}");

                    var chunks =
                        structuredDocument == null
                            ? _chunkingService.Chunk(content, documentName)
                            : _chunkingService.Chunk(structuredDocument, documentName);

                    foreach (var chunk in chunks)
                    {
                        chunk.DocumentId = documentId;
                    }

                    Console.WriteLine($"[CHUNKING COMPLETED] {documentName} -> {chunks.Count} chunks");

                    await _vectorService.StoreAsync(chunks);

                    Console.WriteLine($"[VECTOR STORAGE COMPLETED] {documentName}");

                    if (structuredDocument == null)
                    {
                        await _entityExtractionService.ExtractAndStoreAsync(
                            documentId,
                            content);
                    }
                    else
                    {
                        await _entityExtractionService.StoreStructuredAsync(
                            documentId,
                            structuredDocument);
                    }

                    Console.WriteLine($"[ENTITY STORAGE COMPLETED] {documentName}");

                    await WriteIngestionLogAsync(
                        connection,
                        documentId,
                        "SUCCESS",
                        $"Successfully ingested {documentName}");

                    Console.WriteLine($"[INGESTION SUCCESS] {documentName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed processing file: {file}");
                    Console.WriteLine(ex.Message);

                    try
                    {
                        await WriteIngestionLogAsync(
                            connection,
                            documentId,
                            "FAILED",
                            ex.Message);
                    }
                    catch
                    {
                        Console.WriteLine("[ERROR] Failed writing ingestion log.");
                    }
                }
            }
        }

        private async Task<Guid> RegisterDocumentAsync(
            NpgsqlConnection connection,
            string filePath,
            string documentName,
            string content,
            StructuredKnowledgeDocument? structuredDocument,
            string fallbackDepartment)
        {
            var metadataJson =
                structuredDocument == null
                    ? "{}"
                    : JsonSerializer.Serialize(structuredDocument.Metadata);

            var tagsJson =
                structuredDocument == null
                    ? "[]"
                    : JsonSerializer.Serialize(structuredDocument.Tags);

            var insertDocumentSql = @"
INSERT INTO documents
(
    document_id,
    file_path,
    file_name,
    file_type,
    department,
    category,
    title,
    version,
    effective_date,
    tags,
    metadata,
    raw_content,
    cleaned_content
)
VALUES
(
    COALESCE(NULLIF(@ExternalDocumentId, '')::uuid, gen_random_uuid()),
    @FilePath,
    @FileName,
    @FileType,
    @Department,
    @Category,
    @Title,
    @Version,
    @EffectiveDate,
    @Tags::jsonb,
    @Metadata::jsonb,
    @RawContent,
    @CleanedContent
)
ON CONFLICT (file_path)
DO UPDATE SET
    file_name = EXCLUDED.file_name,
    file_type = EXCLUDED.file_type,
    department = EXCLUDED.department,
    category = EXCLUDED.category,
    title = EXCLUDED.title,
    version = EXCLUDED.version,
    effective_date = EXCLUDED.effective_date,
    tags = EXCLUDED.tags,
    metadata = EXCLUDED.metadata,
    raw_content = EXCLUDED.raw_content,
    cleaned_content = EXCLUDED.cleaned_content,
    updated_at = now()
RETURNING document_id;";

            return await connection.QuerySingleAsync<Guid>(
                insertDocumentSql,
                new
                {
                    ExternalDocumentId =
                        structuredDocument?.DocumentId ?? string.Empty,

                    FilePath =
                        Path.GetFullPath(filePath),

                    FileName =
                        documentName,

                    FileType =
                        Path.GetExtension(filePath),

                    Department =
                        structuredDocument?.Department ?? fallbackDepartment,

                    Category =
                        structuredDocument?.Category ?? string.Empty,

                    Title =
                        structuredDocument?.Title ?? Path.GetFileNameWithoutExtension(filePath),

                    Version =
                        structuredDocument?.Version ?? string.Empty,

                    EffectiveDate =
                        structuredDocument?.EffectiveDate,

                    Tags =
                        tagsJson,

                    Metadata =
                        metadataJson,

                    RawContent =
                        content,

                    CleanedContent =
                        content
                });
        }

        private async Task ClearDocumentKnowledgeAsync(
            NpgsqlConnection connection,
            Guid documentId)
        {
            await connection.ExecuteAsync(
                "DELETE FROM document_chunks WHERE document_id = @DocumentId;",
                new { DocumentId = documentId });

            await connection.ExecuteAsync(
                "DELETE FROM entities WHERE document_id = @DocumentId;",
                new { DocumentId = documentId });
        }

        private async Task WriteIngestionLogAsync(
            NpgsqlConnection connection,
            Guid documentId,
            string status,
            string message)
        {
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
    gen_random_uuid(),
    NULLIF(@DocumentId, '00000000-0000-0000-0000-000000000000')::uuid,
    @Status,
    @Message
);";

            await connection.ExecuteAsync(
                logSql,
                new
                {
                    DocumentId = documentId,
                    Status = status,
                    Message = message
                });
        }

        private bool IsSupportedFile(string file)
        {
            var extension =
                Path.GetExtension(file).ToLowerInvariant();

            return IsPreparedKnowledgeJson(file)
                   || extension is ".txt" or ".pdf" or ".docx";
        }

        private List<string> SelectFilesForIngestion(
            IEnumerable<string> files)
        {
            var fileList =
                files.ToList();

            var preparedSourcePaths =
                fileList
                    .Where(IsPreparedKnowledgeJson)
                    .Select(GetRawPathForPreparedJson)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(Path.GetFullPath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return fileList
                .Where(IsSupportedFile)
                .Where(file =>
                    IsPreparedKnowledgeJson(file)
                    || !preparedSourcePaths.Contains(Path.GetFullPath(file)))
                .ToList();
        }

        private bool IsPreparedKnowledgeJson(string file)
        {
            return file.EndsWith(
                ".kb.json",
                StringComparison.OrdinalIgnoreCase);
        }

        private string GetRawPathForPreparedJson(string preparedJsonPath)
        {
            var directory =
                Path.GetDirectoryName(preparedJsonPath) ?? string.Empty;

            var baseName =
                Path.GetFileName(preparedJsonPath);

            if (!baseName.EndsWith(".kb.json", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var rawBaseName =
                baseName[..^".kb.json".Length];

            var candidateExtensions =
                new[] { ".pdf", ".docx", ".txt" };

            return candidateExtensions
                .Select(extension => Path.Combine(directory, rawBaseName + extension))
                .FirstOrDefault(File.Exists)
                ?? string.Empty;
        }

        private string GetDepartmentFromPath(string filePath)
        {
            var root =
                Path.GetFullPath(_settings.DirectoryPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var fullPath =
                Path.GetFullPath(filePath);

            var relativePath =
                Path.GetRelativePath(root, fullPath);

            var firstSegment =
                relativePath
                    .Split(
                        new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                        StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();

            return firstSegment ?? string.Empty;
        }
    }
}
