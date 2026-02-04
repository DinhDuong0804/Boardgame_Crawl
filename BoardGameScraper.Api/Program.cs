using BoardGameScraper.Api;
using BoardGameScraper.Api.Data;
using BoardGameScraper.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// ============================================
// DATABASE - PostgreSQL with Entity Framework
// ============================================
var connectionString = builder.Configuration.GetConnectionString("PostgreSQL")
    ?? "Host=localhost;Port=5432;Database=boardgame_cafe;Username=boardgame;Password=BoardGame@2026";

builder.Services.AddDbContext<BoardGameDbContext>(options =>
    options.UseNpgsql(connectionString));

// ============================================
// APPLICATION SERVICES
// ============================================
builder.Services.AddScoped<GameService>();

// Scraper Services
builder.Services.AddSingleton<BggDiscoveryService>();
builder.Services.AddSingleton<BggApiClient>();
builder.Services.AddSingleton<DataExportService>();
builder.Services.AddSingleton<StateManager>();
builder.Services.AddSingleton<RulebookScraperService>();
builder.Services.AddSingleton<WikidataEnrichmentService>();

// New PDF Services (C# based)
builder.Services.AddScoped<PdfService>();
builder.Services.AddSingleton<BggPlaywrightService>();
builder.Services.AddSingleton<BackgroundScraperService>();

// SignalR
builder.Services.AddSignalR();

// BGG Download Service with HttpClient and Cookies
builder.Services.AddHttpClient<BggPdfDownloadService>((sp, client) =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    client.Timeout = TimeSpan.FromMinutes(5); // PDFs can be large
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    UseCookies = true,
    CookieContainer = new System.Net.CookieContainer(),
    AllowAutoRedirect = true
});

// ============================================
// BACKGROUND WORKERS (Optional - can be disabled)
// ============================================
var enableBackgroundWorkers = builder.Configuration.GetValue<bool>("Scraper:EnableBackgroundWorkers", false);
if (enableBackgroundWorkers)
{
    builder.Services.AddHostedService<ScraperWorker>();
}

// ============================================
// HTTP CLIENTS with Resilience (Polly)
// ============================================

// BGG Discovery Service
builder.Services.AddHttpClient<BggDiscoveryService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var token = config["BggApi:AuthToken"];

    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

    if (!string.IsNullOrEmpty(token))
    {
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }
})
.AddStandardResilienceHandler();

// BGG API Client
builder.Services.AddHttpClient<BggApiClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var token = config["BggApi:AuthToken"];

    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

    if (!string.IsNullOrEmpty(token))
    {
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }
})
.AddStandardResilienceHandler();

// Rulebook Scraper
builder.Services.AddHttpClient<RulebookScraperService>((sp, client) =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddStandardResilienceHandler();

// Wikidata SPARQL
builder.Services.AddHttpClient<WikidataEnrichmentService>((sp, client) =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BoardGameScraper/1.0 (https://github.com/example; compatible)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.Timeout = TimeSpan.FromSeconds(60);
})
.AddStandardResilienceHandler();

// ============================================
// BUILD APP
// ============================================
var app = builder.Build();

// ============================================
// DATABASE MIGRATION (Optional in Development)
// ============================================
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BoardGameDbContext>();

    // Ensure database is created (use migrations in production)
    try
    {
        await db.Database.EnsureCreatedAsync();
        app.Logger.LogInformation("Database connection verified");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Database not available. Make sure PostgreSQL is running.");
    }
}

// ============================================
// CONFIGURE PIPELINE
// ============================================
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Enable CORS for frontend
app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

app.UseHttpsRedirection();

// Serve static files from wwwroot (Admin Dashboard)
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();
app.MapControllers();

// SignalR Hubs
app.MapHub<BoardGameScraper.Api.Hubs.ScraperHub>("/hubs/scraper");

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Fallback to index.html for SPA routing
app.MapFallbackToFile("index.html");

app.Run();
