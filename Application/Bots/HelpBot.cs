using bot_kit.Application.DTOs;
using bot_kit.Application.Interfaces;

namespace bot_kit.Application.Bots
{
    public class HelpBot : IBot
    {
        private readonly IOllamaService _ollamaService;

        private readonly IVectorService _vectorService;

        public string BotType => "help";

        public HelpBot(
            IOllamaService ollamaService,
            IVectorService vectorService)
        {
            _ollamaService = ollamaService;

            _vectorService = vectorService;
        }

        public async Task<BotResponse> HandleAsync(
            BotRequest request,
            CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;

            // =====================================================
            // METADATA SEARCH
            // =====================================================

            var metadata =
                await _vectorService
                    .SearchMetadataAsync(
                        request.Prompt);

            // =====================================================
            // BUILD METADATA CONTEXT
            // =====================================================

            var metadataContext =
                string.Join(
                    "\n",
                    metadata.Select(m =>
                    {
                        if (!string.IsNullOrWhiteSpace(
                            m.RelationshipType))
                        {
                            return
                                $"{m.EntityName} -> {m.RelationshipType} -> {m.RelatedEntityName}";
                        }

                        return
                            $"{m.EntityType}: {m.EntityName}";
                    }));

            Console.WriteLine(
                "=== METADATA CONTEXT ===");

            Console.WriteLine(metadataContext);

            // =====================================================
            // HYBRID VECTOR SEARCH
            // =====================================================

            var chunks =
                await _vectorService.SearchAsync(
                    request.Prompt,
                    5);

            // =====================================================
            // RANK + LIMIT CONTEXT
            // =====================================================

            var filtered =
                chunks
                    .OrderBy(c => c.Distance)
                    .Take(5)
                    .ToList();

            // =====================================================
            // DEBUG LOGGING
            // =====================================================

            Console.WriteLine(
                "=== DOCUMENT CONTEXT ===");

            foreach (var c in filtered)
            {
                Console.WriteLine(
                    $"Distance: {c.Distance}");

                Console.WriteLine(
                    $"Document: {c.DocumentName}");

                Console.WriteLine(c.Content);

                Console.WriteLine("--------------------------------");
            }

            // =====================================================
            // BUILD DOCUMENT CONTEXT
            // =====================================================

            var documentContext =
                string.Join(
                    "\n\n",
                    filtered.Select(c =>
                        $"""
                        [Document: {c.DocumentName}]
                        [Chunk: {c.ChunkIndex}]
                        
                        {c.Content}
                        """));

            // =====================================================
            // MERGE FULL CONTEXT
            // =====================================================

            var fullContext =
                $"""
                ====================
                METADATA CONTEXT
                ====================

                {metadataContext}

                ====================
                DOCUMENT CONTEXT
                ====================

                {documentContext}
                """;

            // =====================================================
            // BUILD PROMPT
            // =====================================================

            var prompt =
                BuildPrompt(
                    request.Prompt,
                    fullContext);

            // =====================================================
            // GENERATE RESPONSE
            // =====================================================

            var result =
                await _ollamaService.GenerateAsync(
                    prompt,
                    cancellationToken);

            // =====================================================
            // RETURN RESPONSE
            // =====================================================

            return new BotResponse
            {
                Response = result,

                ModelUsed = "qwen",

                ResponseTimeMs =
                    (long)(DateTime.UtcNow - startTime)
                    .TotalMilliseconds,

                Sources =
                    filtered
                        .Select(c => c.DocumentName)
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x))
                        .Distinct()
                        .ToList()
            };
        }

        private string BuildPrompt(
            string userPrompt,
            string context)
        {
            return $@"
You are an enterprise knowledge assistant.

Your job is to answer ONLY using the provided context.

IMPORTANT RULES:
- Use metadata context first when available
- Use relationship information carefully
- Do NOT invent names, roles, departments, or reporting structures
- Do NOT hallucinate policies
- Do NOT generate placeholders like [Insert Name]

If information is unavailable, say:
'Information not available in knowledge base.'

Prefer:
- concise factual responses
- direct answers
- structured answers when useful

If relationship context exists,
explain relationships clearly.

========================
CONTEXT
========================

{context}

========================
QUESTION
========================

{userPrompt}

========================
ANSWER
========================
";
        }
    }
}