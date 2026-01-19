using System.Text.Json.Serialization;

namespace BoardGameScraper.Api.Models;

public class ScraperState
{
    public int LastPageRank { get; set; } = 1;
    public int LastGameIdSequence { get; set; } = 1;
    
    // Separate tracking for each phase
    public HashSet<int> ProcessedRankIds { get; set; } = new();
    public HashSet<int> ProcessedSequenceIds { get; set; } = new();

    // Legacy field for migration (if exists in old json)
    public HashSet<int> ProcessedIds { get; set; } = new();
}
