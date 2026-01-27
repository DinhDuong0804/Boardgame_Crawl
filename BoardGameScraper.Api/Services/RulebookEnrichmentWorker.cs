using System.Text.Json;
using BoardGameScraper.Api.Models;

namespace BoardGameScraper.Api.Services;

/// <summary>
/// Background worker to enrich existing game data with rulebook URLs
/// This runs as Phase 3 after the main BGG scraping is complete
/// </summary>
public class RulebookEnrichmentWorker : BackgroundService
{
    private readonly RulebookScraperService _rulebookService;
    private readonly WikidataEnrichmentService _wikidataService;
    private readonly ILogger<RulebookEnrichmentWorker> _logger;
    private readonly IConfiguration _config;

    private const string InputFile = "bgg_rank.jsonl";
    private const string OutputFile = "bgg_with_rulebooks.jsonl";
    private const string StateFile = "rulebook_state.json";

    public RulebookEnrichmentWorker(
        RulebookScraperService rulebookService,
        WikidataEnrichmentService wikidataService,
        ILogger<RulebookEnrichmentWorker> logger,
        IConfiguration config)
    {
        _rulebookService = rulebookService;
        _wikidataService = wikidataService;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for main scraper to finish or check if we should run separately
        var enableRulebookPhase = _config.GetValue<bool>("Scraper:EnableRulebookPhase", false);

        if (!enableRulebookPhase)
        {
            _logger.LogInformation("Rulebook enrichment phase is disabled. Set Scraper:EnableRulebookPhase=true to enable.");
            return;
        }

        _logger.LogInformation("=== STARTING PHASE 3: RULEBOOK ENRICHMENT ===");

        await Task.Delay(2000, stoppingToken); // Wait for system startup

        var processedIds = await LoadStateAsync(stoppingToken);
        var batchSize = _config.GetValue<int>("Scraper:RulebookBatchSize", 10);

        try
        {
            // Read existing games from JSONL
            var games = await ReadGamesFromJsonlAsync(InputFile, stoppingToken);
            _logger.LogInformation("Loaded {Count} games to enrich with rulebooks", games.Count);

            var gamesToProcess = games.Where(g => !processedIds.Contains(g.BggId)).ToList();
            _logger.LogInformation("Processing {Count} games (skipping {Skipped} already processed)",
                gamesToProcess.Count, games.Count - gamesToProcess.Count);

            // Process in batches
            foreach (var batch in gamesToProcess.Chunk(batchSize))
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                await ProcessBatchAsync(batch, stoppingToken);

                // Update state
                foreach (var game in batch)
                {
                    processedIds.Add(game.BggId);
                }
                await SaveStateAsync(processedIds, stoppingToken);

                // Rate limiting
                await Task.Delay(3000, stoppingToken);
            }

            _logger.LogInformation("=== RULEBOOK ENRICHMENT COMPLETE ===");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Rulebook enrichment stopped.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in rulebook enrichment phase");
        }
    }

    private async Task ProcessBatchAsync(GameItem[] batch, CancellationToken ct)
    {
        var bggIds = batch.Select(g => g.BggId).ToList();

        _logger.LogInformation("Processing batch of {Count} games for rulebooks: {Ids}",
            batch.Length, string.Join(", ", bggIds));

        // Fetch rulebooks from BGG
        var rulebookResults = await _rulebookService.GetRulebooksForGamesAsync(bggIds, ct);

        // Fetch Wikidata enrichment
        var wikidataResults = await _wikidataService.GetGameInfoBatchAsync(bggIds, ct);

        // Enrich games and save
        foreach (var game in batch)
        {
            // Add rulebooks
            if (rulebookResults.TryGetValue(game.BggId, out var rulebooks))
            {
                game.RulebookUrls = rulebooks;
            }

            // Add Wikidata info (could extend GameItem for these)
            if (wikidataResults.TryGetValue(game.BggId, out var wikidata))
            {
                _logger.LogDebug("Game {Id} has Wikidata: {WikidataId}, Wikipedia: {Wiki}",
                    game.BggId, wikidata.WikidataId, wikidata.WikipediaUrl);
                // TODO: Add WikidataId, WikipediaUrl fields to GameItem if needed
            }
        }

        // Append to output file
        await AppendGamesToOutputAsync(batch, ct);
    }

    private async Task<List<GameItem>> ReadGamesFromJsonlAsync(string filePath, CancellationToken ct)
    {
        var games = new List<GameItem>();

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Input file {File} not found", filePath);
            return games;
        }

        var lines = await File.ReadAllLinesAsync(filePath, ct);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var game = JsonSerializer.Deserialize<GameItem>(line);
                if (game != null)
                {
                    games.Add(game);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse game from line");
            }
        }

        return games;
    }

    private async Task AppendGamesToOutputAsync(GameItem[] games, CancellationToken ct)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };

        await using var writer = new StreamWriter(OutputFile, append: true);
        foreach (var game in games)
        {
            var json = JsonSerializer.Serialize(game, options);
            await writer.WriteLineAsync(json.AsMemory(), ct);
        }

        _logger.LogInformation("Appended {Count} enriched games to {File}", games.Length, OutputFile);
    }

    private async Task<HashSet<int>> LoadStateAsync(CancellationToken ct)
    {
        if (!File.Exists(StateFile))
        {
            return new HashSet<int>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(StateFile, ct);
            var state = JsonSerializer.Deserialize<RulebookState>(json);
            return state?.ProcessedIds ?? new HashSet<int>();
        }
        catch
        {
            return new HashSet<int>();
        }
    }

    private async Task SaveStateAsync(HashSet<int> processedIds, CancellationToken ct)
    {
        var state = new RulebookState { ProcessedIds = processedIds };
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(StateFile, json, ct);
    }

    private class RulebookState
    {
        public HashSet<int> ProcessedIds { get; set; } = new();
    }
}
