using bot_kit.Domain.Entities;
using DocumentFormat.OpenXml.Packaging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace bot_kit.Infrastructure.DocumentProcessing
{
    public class DocumentParser
    {
        public async Task<string> ParseAsync(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            return extension switch
            {
                ".txt" => NormalizeText(await File.ReadAllTextAsync(filePath)),
                ".json" => await ParseJsonAsSearchableTextAsync(filePath),
                ".pdf" => ParsePdf(filePath),
                ".docx" => ParseDocx(filePath),
                _ => string.Empty
            };
        }

        public async Task<StructuredKnowledgeDocument?> ParseStructuredJsonAsync(
            string filePath,
            string fallbackDepartment)
        {
            try
            {
                var json =
                    await File.ReadAllTextAsync(filePath);

                var document =
                    JsonSerializer.Deserialize<StructuredKnowledgeDocument>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                if (document == null)
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(document.Department))
                {
                    document.Department = fallbackDepartment;
                }

                if (string.IsNullOrWhiteSpace(document.Title))
                {
                    document.Title =
                        Path.GetFileNameWithoutExtension(filePath);
                }

                NormalizeStructuredDocument(document);

                return document;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] JSON parsing failed: {ex.Message}");
                return null;
            }
        }

        private string ParsePdf(string path)
        {
            try
            {
                var text = new StringBuilder();

                using (var document = PdfDocument.Open(path))
                {
                    foreach (var page in document.GetPages())
                    {
                        text.AppendLine(page.Text);
                    }
                }

                Console.WriteLine($"[PDF PARSED] Length: {text.Length}");

                return NormalizeText(text.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] PDF parsing failed: {ex.Message}");
                return string.Empty;
            }
        }

        private string ParseDocx(string path)
        {
            try
            {
                using var doc = WordprocessingDocument.Open(path, false);

                var body = doc.MainDocumentPart?.Document?.Body;

                var text = body?.InnerText ?? string.Empty;

                return NormalizeText(text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] DOCX parsing failed: {ex.Message}");
                return string.Empty;
            }
        }

        private async Task<string> ParseJsonAsSearchableTextAsync(string filePath)
        {
            var department =
                Directory.GetParent(filePath)?.Name ?? string.Empty;

            var document =
                await ParseStructuredJsonAsync(
                    filePath,
                    department);

            return document?.ToSearchableText() ?? string.Empty;
        }

        private string NormalizeText(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            content = content
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\t", " ");

            content = Regex.Replace(content, @"\[.*?\]", "");
            content = Regex.Replace(content, @"[ ]{2,}", " ");
            content = Regex.Replace(content, @"\n{3,}", "\n\n");

            return content.Trim();
        }

        private void NormalizeStructuredDocument(
            StructuredKnowledgeDocument document)
        {
            document.Department = NormalizeText(document.Department);
            document.Category = NormalizeText(document.Category);
            document.Title = NormalizeText(document.Title);
            document.Version = NormalizeText(document.Version);

            document.Tags =
                document.Tags
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeText)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            foreach (var entity in document.Entities)
            {
                entity.Name = NormalizeText(entity.Name);
                entity.Type = NormalizeText(entity.Type);

                entity.Aliases =
                    entity.Aliases
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(NormalizeText)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
            }

            foreach (var relationship in document.Relationships)
            {
                relationship.Source = NormalizeText(relationship.Source);
                relationship.Type = NormalizeText(relationship.Type);
                relationship.Target = NormalizeText(relationship.Target);
            }

            document.Content = NormalizeText(document.Content);

            foreach (var section in document.Sections)
            {
                section.Heading = NormalizeText(section.Heading);
                section.Content = NormalizeText(section.Content);

                section.Tags =
                    section.Tags
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(NormalizeText)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                section.Entities =
                    section.Entities
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(NormalizeText)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
            }
        }
    }
}
