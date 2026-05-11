using bot_kit.Application.Interfaces;
using System.Net.Http.Json;
using System.Text.Json;

namespace bot_kit.Infrastructure.Ollama
{
   

    public class OllamaEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;

        public OllamaEmbeddingService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<List<float>> GenerateEmbeddingAsync(string text)
        {
            var request = new
            {
                model = "nomic-embed-text", // IMPORTANT
                prompt = text
            };

            var response = await _httpClient.PostAsJsonAsync("/api/embeddings", request);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);

            var embedding = doc.RootElement.GetProperty("embedding");

            return embedding.EnumerateArray().Select(x => x.GetSingle()).ToList();
        }
    }
}
