using bot_kit.Application.Interfaces;
using System.Net.Http.Json;
using System.Text.Json;


namespace bot_kit.Infrastructure.Ollama
{
 
    public class OllamaService : IOllamaService
    {
        private readonly HttpClient _httpClient;

        public OllamaService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            var request = new
            {
                model = "qwen2.5:7b", // make configurable later
                prompt = prompt,
                stream = false
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/api/generate",
                request,
                cancellationToken
            );

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            using var doc = JsonDocument.Parse(json);

            // Ollama returns: { response: "text..." }
            var result = doc.RootElement.GetProperty("response").GetString();

            return result ?? string.Empty;
        }
    }
}
