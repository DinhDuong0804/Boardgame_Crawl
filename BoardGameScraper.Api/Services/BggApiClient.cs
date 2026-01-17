using System.Xml.Linq;
using BoardGameScraper.Api.Models;

namespace BoardGameScraper.Api.Services;

public class BggApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BggApiClient> _logger;

    public BggApiClient(HttpClient httpClient, ILogger<BggApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<GameItem>> GetGamesDetailsAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return new List<GameItem>();

        var idString = string.Join(",", idList);
        var url = $"https://boardgamegeek.com/xmlapi2/thing?id={idString}&stats=1&versions=1";

        _logger.LogInformation("Enriching {Count} items...", idList.Count);
        
        try 
        {
            using var stream = await _httpClient.GetStreamAsync(url, ct);
            var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
            
            var items = doc.Root?.Elements("item"); 
            if (items == null) return new List<GameItem>();

            var results = new List<GameItem>();
            foreach (var item in items)
            {
                var game = ParseGameItem(item);
                if (game != null) results.Add(game);
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError("API Error: {Message}", ex.Message);
            return new List<GameItem>();
        }
    }

    private GameItem? ParseGameItem(XElement item)
    {
        try 
        {
            var g = new GameItem();
            g.BggId = int.TryParse(item.Attribute("id")?.Value, out int id) ? id : 0;
            
            var nameElem = item.Elements("name").FirstOrDefault(e => (string?)e.Attribute("type") == "primary") 
                           ?? item.Element("name");
            g.Name = nameElem?.Attribute("value")?.Value;
            
            g.Year = int.TryParse(item.Element("yearpublished")?.Attribute("value")?.Value, out int y) ? y : null;
            g.Description = item.Element("description")?.Value;
            
            g.MinPlayers = int.TryParse(item.Element("minplayers")?.Attribute("value")?.Value, out int minp) ? minp : null;
            g.MaxPlayers = int.TryParse(item.Element("maxplayers")?.Attribute("value")?.Value, out int maxp) ? maxp : null;
            g.MinTime = int.TryParse(item.Element("minplaytime")?.Attribute("value")?.Value, out int mint) ? mint : null;
            g.MaxTime = int.TryParse(item.Element("maxplaytime")?.Attribute("value")?.Value, out int maxt) ? maxt : null;
            
            var img = item.Element("image")?.Value;
            if (!string.IsNullOrEmpty(img)) g.ImageUrls.Add(img);
            var thumb = item.Element("thumbnail")?.Value;
            if (!string.IsNullOrEmpty(thumb)) g.ImageUrls.Add(thumb);

            foreach (var link in item.Elements("link"))
            {
                var type = (string?)link.Attribute("type");
                var value = (string?)link.Attribute("value");
                var linkId = (string?)link.Attribute("id");
                
                if (string.IsNullOrEmpty(value)) continue;
                
                var displayVal = !string.IsNullOrEmpty(linkId) ? $"{value}:{linkId}" : value;

                switch (type)
                {
                    case "boardgamedesigner": g.Designers.Add(displayVal); break;
                    case "boardgameartist": g.Artists.Add(displayVal); break;
                    case "boardgamepublisher": g.Publishers.Add(displayVal); break;
                    case "boardgamecategory": g.Categories.Add(displayVal); break;
                    case "boardgamemechanic": g.Mechanics.Add(displayVal); break;
                }
            }

            var stats = item.Element("statistics")?.Element("ratings");
            if (stats != null)
            {
                if (double.TryParse(stats.Element("average")?.Attribute("value")?.Value, out double avg)) 
                    g.AvgRating = avg;
                
                var rankElem = stats.Element("ranks")?.Elements("rank")
                    .FirstOrDefault(r => (string?)r.Attribute("name") == "boardgame" && (string?)r.Attribute("id") == "1");
                
                if (rankElem != null && int.TryParse(rankElem.Attribute("value")?.Value, out int r))
                {
                    g.Rank = r;
                }
            }

            return g;
        }
        catch 
        {
            return null;
        }
    }
}
