using System.Text.Json;
using BoardGameScraper.Api.Models;

namespace BoardGameScraper.Api.Services;

/// <summary>
/// Background worker để dịch game data sang tiếng Việt
/// Đọc từ bgg_with_rulebooks.jsonl và xuất ra bgg_translated.jsonl
/// </summary>
public class TranslationWorker : BackgroundService
{
    private readonly ILogger<TranslationWorker> _logger;
    private readonly TranslationService _translationService;
    private readonly IConfiguration _config;
    private readonly StateManager _stateManager;

    private const string InputFile = "bgg_with_rulebooks.jsonl";
    private const string OutputFile = "bgg_translated.jsonl";
    private const string StateFile = "translation_state.json";

    public TranslationWorker(
        ILogger<TranslationWorker> logger,
        TranslationService translationService,
        IConfiguration config,
        StateManager stateManager)
    {
        _logger = logger;
        _translationService = translationService;
        _config = config;
        _stateManager = stateManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Đợi cho các worker khác khởi động trước
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        var enableTranslation = _config.GetValue<bool>("Scraper:EnableTranslation");
        if (!enableTranslation)
        {
            _logger.LogInformation("Translation is disabled. Set Scraper:EnableTranslation to true to enable.");
            return;
        }

        _logger.LogInformation("Translation Worker started - translating to Vietnamese with LibreTranslate");

        try
        {
            await TranslateGamesAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Translation Worker stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation Worker error");
        }
    }

    private async Task TranslateGamesAsync(CancellationToken ct)
    {
        // Load state
        var state = await LoadStateAsync();
        var processedIds = state.ProcessedBggIds.ToHashSet();

        // Check if input file exists
        if (!File.Exists(InputFile))
        {
            _logger.LogWarning("Input file {File} not found. Waiting for rulebook enrichment to complete.", InputFile);
            return;
        }

        // Read all games from input
        var lines = await File.ReadAllLinesAsync(InputFile, ct);
        var gamesToProcess = new List<GameItem>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var game = JsonSerializer.Deserialize<GameItem>(line);
                if (game != null && !processedIds.Contains(game.BggId))
                {
                    gamesToProcess.Add(game);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse game line");
            }
        }

        _logger.LogInformation("Found {Count} games to translate", gamesToProcess.Count);

        var batchSize = _config.GetValue<int>("Scraper:TranslationBatchSize", 5);
        var processed = 0;

        foreach (var game in gamesToProcess)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                _logger.LogInformation("Translating game: {Name} (BGG ID: {Id})", game.Name, game.BggId);

                // Translate name (nhiều game có tên giữ nguyên)
                game.NameVi = await TranslateWithRetryAsync(game.Name, ct);

                // Translate description (quan trọng nhất)
                if (!string.IsNullOrEmpty(game.Description))
                {
                    // Truncate description if too long (LibreTranslate có giới hạn)
                    var descToTranslate = game.Description.Length > 2000
                        ? game.Description[..2000] + "..."
                        : game.Description;

                    game.DescriptionVi = await TranslateWithRetryAsync(descToTranslate, ct);
                }

                // Translate categories
                if (game.Categories != null && game.Categories.Count > 0)
                {
                    game.CategoryVi = new List<string>();
                    foreach (var cat in game.Categories.Take(10)) // Limit to first 10
                    {
                        // Extract just the name part (before the colon)
                        var catName = cat.Contains(':') ? cat.Split(':')[0] : cat;
                        var translated = await TranslateWithRetryAsync(catName, ct);
                        game.CategoryVi.Add(translated ?? catName);

                        await Task.Delay(500, ct); // Rate limit
                    }
                }

                // Translate mechanics
                if (game.Mechanics != null && game.Mechanics.Count > 0)
                {
                    game.MechanicVi = new List<string>();
                    foreach (var mech in game.Mechanics.Take(10)) // Limit to first 10
                    {
                        var mechName = mech.Contains(':') ? mech.Split(':')[0] : mech;
                        var translated = await TranslateWithRetryAsync(mechName, ct);
                        game.MechanicVi.Add(translated ?? mechName);

                        await Task.Delay(500, ct); // Rate limit
                    }
                }

                // Save to output file
                await AppendToOutputAsync(game, ct);

                // Update state
                state.ProcessedBggIds.Add(game.BggId);
                state.LastProcessedAt = DateTime.UtcNow;
                await SaveStateAsync(state);

                processed++;
                _logger.LogInformation("Translated {Processed}/{Total}: {Name}",
                    processed, gamesToProcess.Count, game.Name);

                // Rate limiting - LibreTranslate public: 10 requests/min = need 6s+ between requests
                await Task.Delay(TimeSpan.FromSeconds(7), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error translating game {Id}: {Name}", game.BggId, game.Name);
                // Continue with next game
            }
        }

        _logger.LogInformation("Translation completed. Processed {Count} games.", processed);
    }

    private async Task<string?> TranslateWithRetryAsync(string? text, CancellationToken ct, int maxRetries = 3)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var result = await _translationService.TranslateToVietnameseAsync(text, ct);
                if (!string.IsNullOrEmpty(result))
                    return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Translation attempt {Attempt} failed", i + 1);
                if (i < maxRetries - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * (i + 1)), ct);
                }
            }
        }

        return null; // Return null if all retries failed
    }

    private async Task AppendToOutputAsync(GameItem game, CancellationToken ct)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false
        };

        var json = JsonSerializer.Serialize(game, options);
        await File.AppendAllTextAsync(OutputFile, json + Environment.NewLine, ct);
    }

    private async Task<TranslationState> LoadStateAsync()
    {
        if (File.Exists(StateFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(StateFile);
                return JsonSerializer.Deserialize<TranslationState>(json) ?? new TranslationState();
            }
            catch
            {
                return new TranslationState();
            }
        }
        return new TranslationState();
    }

    private async Task SaveStateAsync(TranslationState state)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(state, options);
        await File.WriteAllTextAsync(StateFile, json);
    }

    private class TranslationState
    {
        public HashSet<int> ProcessedBggIds { get; set; } = new();
        public DateTime LastProcessedAt { get; set; }
    }
}
