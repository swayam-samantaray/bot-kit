using bot_kit.Application.Interfaces;
using bot_kit.Domain.Entities;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace bot_kit.Infrastructure.DocumentProcessing
{
    public class ChunkingService : IChunkingService
    {
        private const int MaxChunkSize = 500;
        private const int OverlapSize = 80;

        public List<DocumentChunk> Chunk(string content, string documentName)
        {
            var chunks = new List<DocumentChunk>();

            if (string.IsNullOrWhiteSpace(content))
            {
                return chunks;
            }

            content = FixBrokenLines(content);

            var blocks = ExtractStructuredBlocks(content);
            var buffer = new StringBuilder();
            var chunkIndex = 0;

            foreach (var block in blocks)
            {
                var smallerBlocks = SplitLargeBlock(block);

                foreach (var smallBlock in smallerBlocks)
                {
                    if (buffer.Length + smallBlock.Length <= MaxChunkSize)
                    {
                        buffer.Append(' ').Append(smallBlock);
                    }
                    else
                    {
                        if (buffer.Length > 0)
                        {
                            chunks.Add(CreateChunk(buffer.ToString(), documentName, chunkIndex++));
                        }

                        var overlap = GetOverlap(buffer.ToString());

                        buffer.Clear();
                        buffer.Append(overlap).Append(' ').Append(smallBlock);
                    }
                }
            }

            if (buffer.Length > 0)
            {
                chunks.Add(CreateChunk(buffer.ToString(), documentName, chunkIndex));
            }

            return chunks;
        }

        public List<DocumentChunk> Chunk(
            StructuredKnowledgeDocument document,
            string documentName)
        {
            var chunks = new List<DocumentChunk>();

            if (document.Sections.Count == 0)
            {
                return Chunk(document.ToSearchableText(), documentName)
                    .Select(chunk =>
                    {
                        ApplyDocumentMetadata(
                            chunk,
                            document,
                            string.Empty,
                            document.Tags,
                            new List<string>());

                        return chunk;
                    })
                    .ToList();
            }

            var chunkIndex = 0;

            foreach (var section in document.Sections)
            {
                if (string.IsNullOrWhiteSpace(section.Content))
                {
                    continue;
                }

                var sectionChunks =
                    Chunk(
                        BuildSectionText(document, section),
                        documentName);

                foreach (var chunk in sectionChunks)
                {
                    chunk.ChunkIndex = chunkIndex++;

                    var tags =
                        document.Tags
                            .Concat(section.Tags)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                    ApplyDocumentMetadata(
                        chunk,
                        document,
                        section.Heading,
                        tags,
                        section.Entities);

                    chunks.Add(chunk);
                }
            }

            return chunks;
        }

        private string FixBrokenLines(string text)
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>();
            var current = new StringBuilder();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (current.Length > 0 && !IsNewBlock(trimmed))
                {
                    current.Append(' ').Append(trimmed);
                }
                else
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                    }

                    current.Clear();
                    current.Append(trimmed);
                }
            }

            if (current.Length > 0)
            {
                result.Add(current.ToString());
            }

            return string.Join("\n", result);
        }

        private bool IsNewBlock(string line)
        {
            return Regex.IsMatch(line, @"^\d+\.")
                   || line.Contains(':')
                   || line.Length > 100;
        }

        private List<string> ExtractStructuredBlocks(string content)
        {
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var blocks = new List<string>();
            var current = new StringBuilder();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (IsStrongBoundary(trimmed))
                {
                    if (current.Length > 0)
                    {
                        blocks.Add(current.ToString());
                        current.Clear();
                    }
                }

                current.Append(' ').Append(trimmed);

                if (trimmed.Contains(':') && trimmed.Length < 100)
                {
                    blocks.Add(current.ToString());
                    current.Clear();
                }
            }

            if (current.Length > 0)
            {
                blocks.Add(current.ToString());
            }

            return blocks;
        }

        private DocumentChunk CreateChunk(string text, string docName, int index)
        {
            return new DocumentChunk
            {
                Id = Guid.NewGuid().ToString(),
                Content = text.Trim(),
                DocumentName = docName,
                ChunkIndex = index
            };
        }

        private string BuildSectionText(
            StructuredKnowledgeDocument document,
            StructuredSection section)
        {
            var header = new List<string>
            {
                $"Department: {document.Department}",
                $"Category: {document.Category}",
                $"Document: {document.Title}",
                $"Section: {section.Heading}"
            };

            var tags =
                document.Tags
                    .Concat(section.Tags)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (tags.Any())
            {
                header.Add($"Tags: {string.Join(", ", tags)}");
            }

            if (section.Entities.Any())
            {
                header.Add($"Entities: {string.Join(", ", section.Entities)}");
            }

            return string.Join("\n", header) + "\n\n" + section.Content;
        }

        private void ApplyDocumentMetadata(
            DocumentChunk chunk,
            StructuredKnowledgeDocument document,
            string sectionHeading,
            List<string> tags,
            List<string> entityNames)
        {
            chunk.Department = document.Department;
            chunk.Category = document.Category;
            chunk.Title = document.Title;
            chunk.SectionHeading = sectionHeading;
            chunk.Tags = tags;
            chunk.EntityNames = entityNames;

            chunk.MetadataJson =
                JsonSerializer.Serialize(
                    new
                    {
                        document.DocumentId,
                        document.Department,
                        document.Category,
                        document.Title,
                        document.Version,
                        document.EffectiveDate,
                        document.Metadata,
                        Section = sectionHeading,
                        Tags = tags,
                        Entities = entityNames
                    });
        }

        private string GetOverlap(string text)
        {
            if (text.Length <= OverlapSize)
            {
                return text;
            }

            return text.Substring(text.Length - OverlapSize);
        }

        private bool IsStrongBoundary(string line)
        {
            return Regex.IsMatch(line, @"^\d+\.")
                   || line.EndsWith(':')
                   || line.Length > 120;
        }

        private List<string> SplitLargeBlock(string block)
        {
            var result = new List<string>();

            if (block.Length <= MaxChunkSize)
            {
                result.Add(block);
                return result;
            }

            var sentences = Regex.Split(block, @"(?<=[.!?])\s+");
            var buffer = new StringBuilder();

            foreach (var sentence in sentences)
            {
                if (buffer.Length + sentence.Length <= MaxChunkSize)
                {
                    buffer.Append(' ').Append(sentence);
                }
                else
                {
                    result.Add(buffer.ToString().Trim());
                    buffer.Clear();
                    buffer.Append(sentence);
                }
            }

            if (buffer.Length > 0)
            {
                result.Add(buffer.ToString().Trim());
            }

            return result;
        }
    }
}
