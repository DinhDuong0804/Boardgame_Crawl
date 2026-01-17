using System.Text.Json;
using BoardGameScraper.Api.Models;

namespace BoardGameScraper.Api.Services;

public class DataExportService
{
    private const string FileName = "bgg_data_dotnet.json";
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
            var options = new JsonSerializerOptions { WriteIndented = true };
            List<GameItem> existingData = new();
            
            if (File.Exists(FileName))
            {
                try 
                {
                    using var stream = File.OpenRead(FileName);
                    existingData = await JsonSerializer.DeserializeAsync<List<GameItem>>(stream, options, ct) ?? new();
                }
                catch
                {
                    _logger.LogWarning("Could not parse existing file, starting fresh/appending.");
                }
            }

            existingData.AddRange(games);

            using var writeStream = File.Create(FileName);
            await JsonSerializer.SerializeAsync(writeStream, existingData, options, ct);
            
            _logger.LogInformation("Saved {Count} games to {File}. Total: {Total}", games.Count, FileName, existingData.Count);
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
