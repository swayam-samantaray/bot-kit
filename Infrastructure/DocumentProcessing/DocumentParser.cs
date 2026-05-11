using DocumentFormat.OpenXml.Packaging;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace bot_kit.Infrastructure.DocumentProcessing
{
    public class DocumentParser
    {
        public async Task<string> ParseAsync(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLower();

            return extension switch
            {
                ".txt" => NormalizeText(await File.ReadAllTextAsync(filePath)),
                ".pdf" => ParsePdf(filePath),
                ".docx" => ParseDocx(filePath),
                _ => string.Empty
            };
        }

        // ✅ PDF Parsing
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

        // ✅ DOCX Parsing
        private string ParseDocx(string path)
        {
            try
            {
                using var doc = WordprocessingDocument.Open(path, false);

                var body = doc.MainDocumentPart?.Document.Body;

                var text = body?.InnerText ?? string.Empty;

                return NormalizeText(text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] DOCX parsing failed: {ex.Message}");
                return string.Empty;
            }
        }

        // ✅ Improved Normalization (Important)
        private string NormalizeText(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return string.Empty;

            content = content
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\t", " ");

            // remove placeholders
            content = Regex.Replace(content, @"\[.*?\]", "");

            // collapse excessive spaces
            content = Regex.Replace(content, @"[ ]{2,}", " ");

            // collapse excessive newlines
            content = Regex.Replace(content, @"\n{3,}", "\n\n");

            return content.Trim();
        }
    }
}