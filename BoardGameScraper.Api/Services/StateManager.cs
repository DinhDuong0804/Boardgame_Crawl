using System.Text.Json;
using BoardGameScraper.Api.Models;

namespace BoardGameScraper.Api.Services;

public class StateManager
{
    private const string StateFileName = "scraper_state.json";
    private ScraperState _state = new();
    private readonly ILogger<StateManager> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isDirty = false;

    public StateManager(ILogger<StateManager> logger)
    {
        _logger = logger;
    }

    public int LastPageRank 
    { 
        get => _state.LastPageRank; 
        set { _state.LastPageRank = value; _isDirty = true; }
    }

    public int LastGameIdSequence
    {
        get => _state.LastGameIdSequence;
        set { _state.LastGameIdSequence = value; _isDirty = true; }
    }

    public async Task LoadStateAsync(CancellationToken ct = default)
    {
        if (File.Exists(StateFileName))
        {
            try
            {
                var json = await File.ReadAllTextAsync(StateFileName, ct);
                _state = JsonSerializer.Deserialize<ScraperState>(json) ?? new();

                // Migration: Move legacy ProcessedIds to ProcessedRankIds (assuming old state was Phase 1)
                if (_state.ProcessedIds != null && _state.ProcessedIds.Count > 0 && _state.ProcessedRankIds.Count == 0)
                {
                    foreach (var id in _state.ProcessedIds)
                    {
                        _state.ProcessedRankIds.Add(id);
                    }
                    _state.ProcessedIds.Clear();
                    _isDirty = true;
                    _logger.LogInformation("Migrated {Count} legacy IDs to Rank state.", _state.ProcessedRankIds.Count);
                }

                _logger.LogInformation("Loaded state: RankPage={Rank}, SequenceId={Id}, RankCount={RCount}, SeqCount={SCount}", 
                    _state.LastPageRank, _state.LastGameIdSequence, _state.ProcessedRankIds.Count, _state.ProcessedSequenceIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load state file");
            }
        }
    }

    public bool IsProcessedRank(int id) => _state.ProcessedRankIds.Contains(id);
    public bool IsProcessedSequence(int id) => _state.ProcessedSequenceIds.Contains(id);

    // Mark batch for Rank phase
    public void MarkBatchRankProcessed(IEnumerable<int> ids)
    {
        foreach (var id in ids)
        {
            if (_state.ProcessedRankIds.Add(id))
            {
                _isDirty = true;
            }
        }
    }

    // Mark batch for Sequence phase
    public void MarkBatchSequenceProcessed(IEnumerable<int> ids)
    {
        foreach (var id in ids)
        {
            if (_state.ProcessedSequenceIds.Add(id))
            {
                _isDirty = true;
            }
        }
    }

    public async Task SaveStateAsync(CancellationToken ct = default)
    {
        if (!_isDirty) return;

        await _lock.WaitAsync(ct);
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(StateFileName, JsonSerializer.Serialize(_state, options), ct);
            _isDirty = false;
            // _logger.LogInformation("State saved."); 
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state");
        }
        finally
        {
            _lock.Release();
        }
    }
}
