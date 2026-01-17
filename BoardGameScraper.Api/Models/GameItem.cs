using System.Text.Json.Serialization;

namespace BoardGameScraper.Api.Models;

public class GameItem
{
    [JsonPropertyName("bgg_id")]
    public int BggId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("min_players")]
    public int? MinPlayers { get; set; }

    [JsonPropertyName("max_players")]
    public int? MaxPlayers { get; set; }

    [JsonPropertyName("min_time")]
    public int? MinTime { get; set; }

    [JsonPropertyName("max_time")]
    public int? MaxTime { get; set; }

    [JsonPropertyName("avg_rating")]
    public double? AvgRating { get; set; }
    
    [JsonPropertyName("rank")]
    public int? Rank { get; set; }

    [JsonPropertyName("image_url")]
    public List<string> ImageUrls { get; set; } = new();

    [JsonPropertyName("designer")]
    public List<string> Designers { get; set; } = new();

    [JsonPropertyName("artist")]
    public List<string> Artists { get; set; } = new();

    [JsonPropertyName("publisher")]
    public List<string> Publishers { get; set; } = new();
        
    [JsonPropertyName("category")]
    public List<string> Categories { get; set; } = new();

    [JsonPropertyName("mechanic")]
    public List<string> Mechanics { get; set; } = new();

    [JsonPropertyName("scraped_at")]
    public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
}
