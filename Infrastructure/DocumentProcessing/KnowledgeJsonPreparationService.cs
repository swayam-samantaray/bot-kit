using bot_kit.Application.Interfaces;
using bot_kit.Domain.Entities;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace bot_kit.Infrastructure.DocumentProcessing
{
    public class KnowledgeJsonPreparationService : IKnowledgeJsonPreparationService
    {
        private readonly KnowledgeBaseSettings _settings;

        private readonly DocumentParser _parser;

        private readonly IOllamaService _ollamaService;

        public KnowledgeJsonPreparationService(
            IOptions<KnowledgeBaseSettings> options,
            DocumentParser parser,
            IOllamaService ollamaService)
        {
            _settings = options.Value;
            _parser = parser;
            _ollamaService = ollamaService;
        }

        public async Task<KnowledgeJsonPreparationResult> PrepareAsync(
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(_settings.DirectoryPath))
            {
                throw new DirectoryNotFoundException(
                    "Knowledge base folder not found.");
            }

            var result = new KnowledgeJsonPreparationResult();

            var files =
                Directory.GetFiles(
                    _settings.DirectoryPath,
                    "*.*",
                    SearchOption.AllDirectories)
                .Where(IsRawKnowledgeFile)
                .ToList();

            result.ScannedFiles = files.Count;

            foreach (var file in files)
            {
                var outputPath =
                    GetPreparedJsonPath(file);

                if (File.Exists(outputPath) && !overwrite)
                {
                    result.SkippedFiles++;
                    result.Messages.Add($"Skipped existing JSON: {outputPath}");
                    continue;
                }

                try
                {
                    var content =
                        await _parser.ParseAsync(file);

                    if (string.IsNullOrWhiteSpace(content))
                    {
                        result.SkippedFiles++;
                        result.Messages.Add($"Skipped empty content: {file}");
                        continue;
                    }

                    var department =
                        GetDepartmentFromPath(file);

                    var metadata =
                        await ExtractMetadataAsync(
                            file,
                            department,
                            content,
                            cancellationToken);

                    metadata.Department =
                        string.IsNullOrWhiteSpace(metadata.Department)
                            ? department
                            : metadata.Department;

                    metadata.Title =
                        string.IsNullOrWhiteSpace(metadata.Title)
                            ? BuildTitleFromFileName(file)
                            : metadata.Title;

                    metadata.Metadata["sourceFile"] =
                        Path.GetFileName(file);

                    metadata.Metadata["sourcePath"] =
                        Path.GetFullPath(file);

                    metadata.Content = content;

                    await WritePreparedJsonAsync(
                        outputPath,
                        metadata,
                        cancellationToken);

                    result.CreatedFiles++;
                    result.OutputFiles.Add(outputPath);
                    result.Messages.Add($"Created JSON: {outputPath}");
                }
                catch (Exception ex)
                {
                    result.FailedFiles++;
                    result.Messages.Add($"Failed {file}: {ex.Message}");
                }
            }

            return result;
        }

        private async Task<StructuredKnowledgeDocument> ExtractMetadataAsync(
            string file,
            string department,
            string content,
            CancellationToken cancellationToken)
        {
            var sample =
                content.Length > 6000
                    ? content[..6000]
                    : content;

            var prompt =
                $$"""
                You are preparing metadata for an enterprise knowledge base.

                Return ONLY valid JSON. Do not wrap it in markdown.
                Do not rewrite or summarize the document content.
                Extract compact retrieval metadata from the document text.

                JSON schema:
                {
                  "department": "{{department}}",
                  "category": "Policy|SOP|Guideline|Announcement|Other",
                  "title": "short document title",
                  "version": "version if present else empty string",
                  "effectiveDate": "yyyy-mm-dd if present else null",
                  "tags": ["5 to 12 short search tags"],
                  "metadata": {
                    "owner": "owner if present else empty string",
                    "appliesTo": "audience if present else empty string"
                  },
                  "entities": [
                    {
                      "name": "entity name",
                      "type": "ROLE|TEAM|PERSON|POLICY_CONCEPT|PROCESS|SYSTEM|LOCATION|OTHER",
                      "aliases": ["optional synonyms"]
                    }
                  ],
                  "relationships": [
                    {
                      "source": "entity name",
                      "type": "APPROVES|OWNS|REQUESTS|ESCALATES|REPORTS_TO|APPLIES_TO|PART_OF|USES|OTHER",
                      "target": "entity name",
                      "confidenceScore": 0.8
                    }
                  ]
                }

                Rules:
                - Prefer entities that help answer questions: roles, teams, policies, systems, processes, approval actors, incident severities.
                - Relationship source and target must exactly match names from entities.
                - Keep tags short and human-searchable.
                - Use the folder department unless the document clearly says otherwise.
                - If unknown, use empty string, empty array, or null.

                Source file: {{Path.GetFileName(file)}}
                Folder department: {{department}}

                DOCUMENT TEXT:
                {{sample}}
                """;

            var response =
                await _ollamaService.GenerateAsync(
                    prompt,
                    cancellationToken);

            var json =
                ExtractJsonObject(response);

            var document =
                JsonSerializer.Deserialize<StructuredKnowledgeDocument>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            return document ?? new StructuredKnowledgeDocument();
        }

        private async Task WritePreparedJsonAsync(
            string outputPath,
            StructuredKnowledgeDocument document,
            CancellationToken cancellationToken)
        {
            var json =
                JsonSerializer.Serialize(
                    document,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            await File.WriteAllTextAsync(
                outputPath,
                json,
                cancellationToken);
        }

        private string ExtractJsonObject(string response)
        {
            var trimmed =
                response.Trim();

            if (trimmed.StartsWith("```"))
            {
                trimmed =
                    Regex.Replace(trimmed, @"^```(?:json)?", "", RegexOptions.IgnoreCase)
                        .Trim();

                trimmed =
                    Regex.Replace(trimmed, @"```$", "")
                        .Trim();
            }

            var start =
                trimmed.IndexOf('{');

            var end =
                trimmed.LastIndexOf('}');

            if (start < 0 || end <= start)
            {
                throw new InvalidOperationException(
                    "Ollama did not return a JSON object.");
            }

            return trimmed[start..(end + 1)];
        }

        private bool IsRawKnowledgeFile(string file)
        {
            var extension =
                Path.GetExtension(file).ToLowerInvariant();

            if (extension == ".json")
            {
                return false;
            }

            return extension is ".txt" or ".pdf" or ".docx";
        }

        private string GetPreparedJsonPath(string file)
        {
            var directory =
                Path.GetDirectoryName(file) ?? _settings.DirectoryPath;

            var fileName =
                Path.GetFileNameWithoutExtension(file);

            return Path.Combine(
                directory,
                $"{fileName}.kb.json");
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

        private string BuildTitleFromFileName(string file)
        {
            var name =
                Path.GetFileNameWithoutExtension(file)
                    .Replace("-", " ")
                    .Replace("_", " ");

            return Regex.Replace(
                name,
                @"\s+",
                " ")
                .Trim();
        }
    }
}
