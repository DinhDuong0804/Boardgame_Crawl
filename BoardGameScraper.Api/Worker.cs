using BoardGameScraper.Api.Services;

namespace BoardGameScraper.Api;

public class ScraperWorker : BackgroundService
{
    private readonly BggDiscoveryService _discoveryService;
    private readonly BggApiClient _apiClient;
    private readonly DataExportService _exportService;
    private readonly ILogger<ScraperWorker> _logger;
    private readonly IConfiguration _config;

    public ScraperWorker(
        BggDiscoveryService discoveryService,
        BggApiClient apiClient,
        DataExportService exportService,
        ILogger<ScraperWorker> logger,
        IConfiguration config)
    {
        _discoveryService = discoveryService;
        _apiClient = apiClient;
        _exportService = exportService;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scraper Worker Started.");
        
        await Task.Delay(1000, stoppingToken);

        var batchSize = _config.GetValue<int>("Scraper:BatchSize", 20);
        var startPage = _config.GetValue<int>("Scraper:StartPage", 1);
        
        // --- Phase 1: Rank Based ---
        _logger.LogInformation("=== STARTING PHASE 1: RANK BASED SCRAPING ===");
        _exportService.OutputFileName = "bgg_rank.json";
        
        var idsBuffer = new List<int>();

        try
        {
            await foreach (var id in _discoveryService.DiscoverIdsByRankAsync(startPage, stoppingToken))
            {
                idsBuffer.Add(id);

                if (idsBuffer.Count >= batchSize)
                {
                    await ProcessBatchAsync(idsBuffer, stoppingToken);
                    idsBuffer.Clear();
                }
            }
            
            if (idsBuffer.Count > 0)
            {
                await ProcessBatchAsync(idsBuffer, stoppingToken);
                idsBuffer.Clear();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Phase 1");
        }

        if (stoppingToken.IsCancellationRequested) return;

        // --- Phase 2: ID Sequence Based ---
        _logger.LogInformation("=== STARTING PHASE 2: ID SEQUENCE SCRAPING ===");
        _exportService.OutputFileName = "bgg_all_ids.json";

        try
        {
            await foreach (var id in _discoveryService.DiscoverIdsBySequenceAsync(stoppingToken))
            {
                idsBuffer.Add(id);

                if (idsBuffer.Count >= batchSize)
                {
                    await ProcessBatchAsync(idsBuffer, stoppingToken);
                    idsBuffer.Clear();
                }
            }
            
            if (idsBuffer.Count > 0)
            {
                await ProcessBatchAsync(idsBuffer, stoppingToken);
                idsBuffer.Clear();
            }
        }
        catch (OperationCanceledException) 
        {
             _logger.LogInformation("Scraper stopped.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Phase 2");
        }
    }

    private async Task ProcessBatchAsync(List<int> ids, CancellationToken ct)
    {
        _logger.LogInformation("Processing batch of {Count} IDs...", ids.Count);
        
        var games = await _apiClient.GetGamesDetailsAsync(ids, ct);
        if (games.Count > 0)
        {
            await _exportService.SaveGamesAsync(games, ct);
        }
        
        await Task.Delay(2000, ct); 
    }
}
