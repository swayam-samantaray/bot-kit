using bot_kit;
using bot_kit.Application.Bots;
using bot_kit.Application.Interfaces;
using bot_kit.Application.Services;
using bot_kit.Infrastructure.DocumentProcessing;
using bot_kit.Infrastructure.Knowledge;
using bot_kit.Infrastructure.Ollama;
using bot_kit.Infrastructure.VectorDB;

using Dapper;

using Npgsql;

using Pgvector;
using Pgvector.Dapper;
using Pgvector.Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// Controllers + OpenAPI
// ==========================================

builder.Services.AddControllers();

builder.Services.AddOpenApi();

// ==========================================
// Bot Registrations
// ==========================================

builder.Services.AddScoped<IBotFactory, BotFactory>();

builder.Services.AddScoped<IBot, HelpBot>();

// Future:
// builder.Services.AddScoped<IBot, MessageBot>();

// ==========================================
// Ollama LLM Service
// ==========================================

builder.Services.AddHttpClient<IOllamaService, OllamaService>(client =>
{
    client.BaseAddress = new Uri("http://localhost:11434");

    client.Timeout = TimeSpan.FromMinutes(5);
});

// ==========================================
// Ollama Embedding Service
// ==========================================

builder.Services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>(client =>
{
    client.BaseAddress = new Uri("http://localhost:11434");

    client.Timeout = TimeSpan.FromMinutes(5);
});

// ==========================================
// Knowledge Base Config
// ==========================================

builder.Services.Configure<KnowledgeBaseSettings>(
    builder.Configuration.GetSection("KnowledgeBase"));

// ==========================================
// Document Processing
// ==========================================

builder.Services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();

builder.Services.AddScoped<IKnowledgeJsonPreparationService, KnowledgeJsonPreparationService>();

builder.Services.AddScoped<DocumentParser>();

builder.Services.AddScoped<IChunkingService, ChunkingService>();

// ==========================================
// PostgreSQL + pgvector Setup
// ==========================================

var connectionString =
    builder.Configuration.GetConnectionString("Postgres");

var dataSourceBuilder =
    new NpgsqlDataSourceBuilder(connectionString);

// Enable pgvector support
dataSourceBuilder.UseVector();

var dataSource = dataSourceBuilder.Build();

// Register datasource
builder.Services.AddSingleton<NpgsqlDataSource>(dataSource);

// Dapper vector handler
SqlMapper.AddTypeHandler(new VectorTypeHandler());

// ==========================================
// Vector Service
// ==========================================

builder.Services.AddScoped<IVectorService, PostgresVectorService>();

builder.Services.AddScoped<IEntityExtractionService, EntityExtractionService>();

// ==========================================
// Build App
// ==========================================

var app = builder.Build();

// ==========================================
// Middleware
// ==========================================

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
