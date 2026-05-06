using bot_kit;
using bot_kit.Application.Bots;
using bot_kit.Application.Interfaces;
using bot_kit.Application.Services;
using bot_kit.Infrastructure.DocumentProcessing;
using bot_kit.Infrastructure.Ollama;
using bot_kit.Infrastructure.VectorDB;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


builder.Services.AddScoped<IBotFactory, BotFactory>();

// Register ALL bots
builder.Services.AddScoped<IBot, HelpBot>();
// later:
// builder.Services.AddScoped<IBot, MessageBot>();


builder.Services.AddHttpClient<IOllamaService, OllamaService>(client =>
{
    client.BaseAddress = new Uri("http://localhost:11434");
    client.Timeout = TimeSpan.FromMinutes(5); // 🔥 increase
});

builder.Services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>(client =>
{
    client.BaseAddress = new Uri("http://localhost:11434");
    client.Timeout = TimeSpan.FromMinutes(5); // 🔥 increase
});

builder.Services.AddHttpClient<IVectorService, ChromaService>(client =>
{
    client.BaseAddress = new Uri("http://localhost:8000");
});

builder.Services.Configure<KnowledgeBaseSettings>(
    builder.Configuration.GetSection("KnowledgeBase"));

builder.Services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();
builder.Services.AddScoped<DocumentParser>();

builder.Services.AddScoped<IChunkingService, ChunkingService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
