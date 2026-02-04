using BoardGameScraper.Api.Services;

namespace BoardGameScraper.Api;

public class ScraperWorker : BackgroundService
{
    private readonly BggDiscoveryService _discoveryService;
    private readonly BggApiClient _apiClient;
    private readonly DataExportService _exportService;
    private readonly StateManager _stateManager;
    private readonly ILogger<ScraperWorker> _logger;
    private readonly IConfiguration _config;

    public ScraperWorker(
        BggDiscoveryService discoveryService,
        BggApiClient apiClient,
        DataExportService exportService,
        StateManager stateManager,
        ILogger<ScraperWorker> logger,
        IConfiguration config)
    {
        _discoveryService = discoveryService;
        _apiClient = apiClient;
        _exportService = exportService;
        _stateManager = stateManager;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scraper Worker Started.");

        await Task.Delay(1000, stoppingToken);
        await _stateManager.LoadStateAsync(stoppingToken);

        var batchSize = _config.GetValue<int>("Scraper:BatchSize", 20);
        var startPage = _stateManager.LastPageRank;

        // --- Phase 1: Rank Based ---
        _logger.LogInformation("=== STARTING PHASE 1: RANK BASED SCRAPING (From Page {Page}) ===", startPage);
        _exportService.OutputFileName = "bgg_rank.jsonl";

        var idsBuffer = new List<int>();

        try
        {
            await foreach (var id in _discoveryService.DiscoverIdsByRankAsync(startPage, ct: stoppingToken))
            {
                if (_stateManager.IsProcessedRank(id))
                    continue;

                idsBuffer.Add(id);

                if (idsBuffer.Count >= batchSize)
                {
                    await ProcessBatchAsync(idsBuffer, stoppingToken, isRankPhase: true);
                    idsBuffer.Clear();
                }
            }

            if (idsBuffer.Count > 0)
            {
                await ProcessBatchAsync(idsBuffer, stoppingToken, isRankPhase: true);
                idsBuffer.Clear();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Phase 1");
        }

        if (stoppingToken.IsCancellationRequested)
            return;

        // --- Phase 2: ID Sequence Based ---
        _logger.LogInformation("=== STARTING PHASE 2: ID SEQUENCE SCRAPING ===");
        _exportService.OutputFileName = "bgg_all_ids.jsonl";

        // Logic resume: LastGameIdSequence là id cuối cùng đã xử lý thành công.
        // Ta sẽ bắt đầu từ id tiếp theo.
        int? resumeId = _stateManager.LastGameIdSequence > 0 ? _stateManager.LastGameIdSequence + 1 : null;
        if (resumeId.HasValue)
        {
            _logger.LogInformation("Resuming Phase 2 from ID: {ResumeId}", resumeId);
        }

        try
        {
            await foreach (var id in _discoveryService.DiscoverIdsBySequenceAsync(resumeId, stoppingToken))
            {
                if (_stateManager.IsProcessedSequence(id))
                    continue;

                idsBuffer.Add(id);

                if (idsBuffer.Count >= batchSize)
                {
                    await ProcessBatchAsync(idsBuffer, stoppingToken, isRankPhase: false);
                    idsBuffer.Clear();
                }
            }

            if (idsBuffer.Count > 0)
            {
                await ProcessBatchAsync(idsBuffer, stoppingToken, isRankPhase: false);
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

    private async Task ProcessBatchAsync(List<int> ids, CancellationToken ct, bool isRankPhase)
    {
        _logger.LogInformation("Processing batch of {Count} IDs ({Phase})...", ids.Count, isRankPhase ? "Rank" : "Seq");

        var games = await _apiClient.GetGamesDetailsAsync(ids, ct);
        if (games.Count > 0)
        {
            await _exportService.SaveGamesAsync(games, ct);

            if (isRankPhase)
            {
                _stateManager.MarkBatchRankProcessed(ids);
            }
            else
            {
                _stateManager.MarkBatchSequenceProcessed(ids);
                // Update cursor for sequence phase
                _stateManager.LastGameIdSequence = ids.Max();
            }

            await _stateManager.SaveStateAsync(ct);
        }

        await Task.Delay(2000, ct);
    }
}
