using System.Text.Json;
using BoardGameScraper.Api.Models;

namespace BoardGameScraper.Api.Services;

public class DataExportService
{
    public string OutputFileName { get; set; } = "bgg_data_dotnet.json";
    private readonly ILogger<DataExportService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DataExportService(ILogger<DataExportService> logger)
    {
        _logger = logger;
    }

    public async Task SaveGamesAsync(List<GameItem> games, CancellationToken ct)
    {
        if (games.Count == 0) return;

        await _lock.WaitAsync(ct);
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = false }; // Single line per item
            
            // Check if file exists to determine if we need a newline prefix (if implementation requires)
            // But for simple AppendAllText, each WriteLine is safer.
            
            using var writer = new StreamWriter(OutputFileName, append: true);
            foreach (var game in games)
            {
                var json = JsonSerializer.Serialize(game, options);
                await writer.WriteLineAsync(json.AsMemory(), ct);
            }
            
            _logger.LogInformation("Appended {Count} games to {File}.", games.Count, OutputFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save data");
        }
        finally
        {
            _lock.Release();
        }
    }
}
