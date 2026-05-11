using bot_kit.Application.Interfaces;
using bot_kit.Domain.Entities;
using System.Text;
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
                return chunks;

            // 🔥 Step 1: Fix broken lines (VERY IMPORTANT)
            content = FixBrokenLines(content);

            // 🔥 Step 2: Split into structured blocks
            var blocks = ExtractStructuredBlocks(content);

            var buffer = new StringBuilder();
            int chunkIndex = 0;

            foreach (var block in blocks)
            {
                var smallerBlocks = SplitLargeBlock(block);

                foreach (var smallBlock in smallerBlocks)
                {
                    if (buffer.Length + smallBlock.Length <= MaxChunkSize)
                    {
                        buffer.Append(" ").Append(smallBlock);
                    }
                    else
                    {
                        if (buffer.Length > 0)
                        {
                            chunks.Add(CreateChunk(buffer.ToString(), documentName, chunkIndex++));
                        }

                        var overlap = GetOverlap(buffer.ToString());

                        buffer.Clear();
                        buffer.Append(overlap).Append(" ").Append(smallBlock);
                    }
                }
            }

            if (buffer.Length > 0)
            {
                chunks.Add(CreateChunk(buffer.ToString(), documentName, chunkIndex));
            }

            return chunks;
        }

        // 🔥 Fix PDF broken lines
        private string FixBrokenLines(string text)
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            var result = new List<string>();
            var current = new StringBuilder();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // If line looks like continuation → append
                if (current.Length > 0 && !IsNewBlock(trimmed))
                {
                    current.Append(" ").Append(trimmed);
                }
                else
                {
                    if (current.Length > 0)
                        result.Add(current.ToString());

                    current.Clear();
                    current.Append(trimmed);
                }
            }

            if (current.Length > 0)
                result.Add(current.ToString());

            return string.Join("\n", result);
        }

        // 🔥 Detect new logical block
        private bool IsNewBlock(string line)
        {
            return Regex.IsMatch(line, @"^\d+\.") // numbered
                   || line.Contains(":")         // key-value
                   || line.Length > 100;         // long paragraph
        }

        // 🔥 Extract meaningful blocks
        private List<string> ExtractStructuredBlocks(string content)
        {
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            var blocks = new List<string>();
            var current = new StringBuilder();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // 🔥 Start new block on strong signals
                if (IsStrongBoundary(trimmed))
                {
                    if (current.Length > 0)
                    {
                        blocks.Add(current.ToString());
                        current.Clear();
                    }
                }

                current.Append(" ").Append(trimmed);

                // 🔥 Flush small key-value blocks immediately
                if (trimmed.Contains(":") && trimmed.Length < 100)
                {
                    blocks.Add(current.ToString());
                    current.Clear();
                }
            }

            if (current.Length > 0)
                blocks.Add(current.ToString());

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

        private string GetOverlap(string text)
        {
            if (text.Length <= OverlapSize)
                return text;

            return text.Substring(text.Length - OverlapSize);
        }


        private bool IsStrongBoundary(string line)
        {
            return Regex.IsMatch(line, @"^\d+\.") // numbered
                   || line.EndsWith(":")          // headings
                   || line.Length > 120;          // large paragraph
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
                    buffer.Append(" ").Append(sentence);
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