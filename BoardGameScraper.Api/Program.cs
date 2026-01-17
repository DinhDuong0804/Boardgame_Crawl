using BoardGameScraper.Api;
using BoardGameScraper.Api.Services;
using Microsoft.Extensions.Http.Resilience;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Register Scraper Services
builder.Services.AddSingleton<BggDiscoveryService>();
builder.Services.AddSingleton<BggApiClient>();
builder.Services.AddSingleton<DataExportService>();

// Register Worker
builder.Services.AddHostedService<ScraperWorker>();

// Configure HttpClient with Resilience (Polly)
// BGG Rate Limit defense: Retry on 429/5xx, and standard timeouts
builder.Services.AddHttpClient<BggDiscoveryService>(client => 
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
})
.AddStandardResilienceHandler();

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
