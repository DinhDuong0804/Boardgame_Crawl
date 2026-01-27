using System.Text.Json;
using System.Web;
using BoardGameScraper.Api.Models;

namespace BoardGameScraper.Api.Services;

/// <summary>
/// Service to enrich game data using Wikidata SPARQL queries
/// Based on the Python wikidata.py spider approach
/// </summary>
public class WikidataEnrichmentService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WikidataEnrichmentService> _logger;

    private const string SparqlEndpoint = "https://query.wikidata.org/sparql";

    // Wikidata property for BGG ID
    private const string BggIdProperty = "P2339";

    public WikidataEnrichmentService(HttpClient httpClient, ILogger<WikidataEnrichmentService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Wikidata requires User-Agent
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BoardGameScraper/1.0 (https://github.com/example; contact@example.com)");
    }

    /// <summary>
    /// Get Wikidata information for a game by its BGG ID
    /// </summary>
    public async Task<WikidataInfo?> GetGameInfoByBggIdAsync(int bggId, CancellationToken ct = default)
    {
        try
        {
            // SPARQL query to find game by BGG ID and get relevant properties
            var query = $@"
                SELECT ?game ?gameLabel ?wikidataId ?wikipedia ?officialWebsite ?image WHERE {{
                    ?game wdt:{BggIdProperty} ""{bggId}"" .
                    BIND(REPLACE(STR(?game), ""http://www.wikidata.org/entity/"", """") AS ?wikidataId)
                    
                    OPTIONAL {{
                        ?wikipedia schema:about ?game ;
                                   schema:isPartOf <https://en.wikipedia.org/> .
                    }}
                    OPTIONAL {{ ?game wdt:P856 ?officialWebsite . }}
                    OPTIONAL {{ ?game wdt:P18 ?image . }}
                    
                    SERVICE wikibase:label {{ bd:serviceParam wikibase:language ""en"" . }}
                }}
                LIMIT 1
            ";

            var result = await ExecuteSparqlQueryAsync(query, ct);

            if (result?.Results?.Bindings == null || result.Results.Bindings.Count == 0)
            {
                _logger.LogDebug("No Wikidata entry found for BGG ID {Id}", bggId);
                return null;
            }

            var binding = result.Results.Bindings[0];

            return new WikidataInfo
            {
                BggId = bggId,
                WikidataId = binding.TryGetValue("wikidataId", out var wdId) ? wdId.Value : null,
                WikipediaUrl = binding.TryGetValue("wikipedia", out var wiki) ? wiki.Value : null,
                OfficialWebsite = binding.TryGetValue("officialWebsite", out var website) ? website.Value : null,
                ImageUrl = binding.TryGetValue("image", out var img) ? img.Value : null,
                Label = binding.TryGetValue("gameLabel", out var label) ? label.Value : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Wikidata for BGG ID {Id}", bggId);
            return null;
        }
    }

    /// <summary>
    /// Batch fetch Wikidata info for multiple games
    /// </summary>
    public async Task<Dictionary<int, WikidataInfo>> GetGameInfoBatchAsync(
        IEnumerable<int> bggIds,
        CancellationToken ct = default)
    {
        var results = new Dictionary<int, WikidataInfo>();
        var idList = bggIds.ToList();

        if (idList.Count == 0)
            return results;

        try
        {
            // Build VALUES clause for batch query
            var valuesClause = string.Join(" ", idList.Select(id => $"\"{id}\""));

            var query = $@"
                SELECT ?game ?gameLabel ?bggId ?wikidataId ?wikipedia ?officialWebsite ?image WHERE {{
                    VALUES ?bggId {{ {valuesClause} }}
                    ?game wdt:{BggIdProperty} ?bggId .
                    BIND(REPLACE(STR(?game), ""http://www.wikidata.org/entity/"", """") AS ?wikidataId)
                    
                    OPTIONAL {{
                        ?wikipedia schema:about ?game ;
                                   schema:isPartOf <https://en.wikipedia.org/> .
                    }}
                    OPTIONAL {{ ?game wdt:P856 ?officialWebsite . }}
                    OPTIONAL {{ ?game wdt:P18 ?image . }}
                    
                    SERVICE wikibase:label {{ bd:serviceParam wikibase:language ""en"" . }}
                }}
            ";

            var result = await ExecuteSparqlQueryAsync(query, ct);

            if (result?.Results?.Bindings != null)
            {
                foreach (var binding in result.Results.Bindings)
                {
                    if (binding.TryGetValue("bggId", out var bggIdValue) &&
                        int.TryParse(bggIdValue.Value, out var bggId))
                    {
                        results[bggId] = new WikidataInfo
                        {
                            BggId = bggId,
                            WikidataId = binding.TryGetValue("wikidataId", out var wdId) ? wdId.Value : null,
                            WikipediaUrl = binding.TryGetValue("wikipedia", out var wiki) ? wiki.Value : null,
                            OfficialWebsite = binding.TryGetValue("officialWebsite", out var website) ? website.Value : null,
                            ImageUrl = binding.TryGetValue("image", out var img) ? img.Value : null,
                            Label = binding.TryGetValue("gameLabel", out var label) ? label.Value : null
                        };
                    }
                }
            }

            _logger.LogInformation("Fetched Wikidata for {Count}/{Total} games", results.Count, idList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch Wikidata fetch");
        }

        return results;
    }

    private async Task<SparqlResult?> ExecuteSparqlQueryAsync(string query, CancellationToken ct)
    {
        var encodedQuery = HttpUtility.UrlEncode(query.Trim());
        var url = $"{SparqlEndpoint}?query={encodedQuery}&format=json";

        var response = await _httpClient.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("SPARQL query failed: {Status}", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<SparqlResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}

public class WikidataInfo
{
    public int BggId { get; set; }
    public string? WikidataId { get; set; }
    public string? WikipediaUrl { get; set; }
    public string? OfficialWebsite { get; set; }
    public string? ImageUrl { get; set; }
    public string? Label { get; set; }
}

// SPARQL result models
public class SparqlResult
{
    public SparqlResults? Results { get; set; }
}

public class SparqlResults
{
    public List<Dictionary<string, SparqlBinding>>? Bindings { get; set; }
}

public class SparqlBinding
{
    public string? Type { get; set; }
    public string? Value { get; set; }
}
