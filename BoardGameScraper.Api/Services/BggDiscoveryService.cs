using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace BoardGameScraper.Api.Services;

public class BggDiscoveryService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BggDiscoveryService> _logger;
    private const string BaseUrl = "https://boardgamegeek.com/browse/boardgame/page/";
    private static readonly Regex IdRegex = new(@"/boardgame/(\d+)/", RegexOptions.Compiled);

    public BggDiscoveryService(HttpClient httpClient, ILogger<BggDiscoveryService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async IAsyncEnumerable<int> DiscoverIdsAsync(int startPage = 1, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        int page = startPage;
        
        while (!ct.IsCancellationRequested)
        {
            _logger.LogInformation("Scraping page {Page}", page);
            var url = $"{BaseUrl}{page}";
            
            string html = "";
            try 
            {
                html = await _httpClient.GetStringAsync(url, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error fetching page {Page}: {Message}", page, ex.Message);
                // Pause and retry logic should technically be in Polly, but here we break loop to avoid infinite error spam if offline
                await Task.Delay(5000, ct);
                continue; 
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var links = doc.DocumentNode.SelectNodes("//a[contains(@href, '/boardgame/')]");
            
            if (links == null || links.Count == 0)
            {
                _logger.LogWarning("No links found on page {Page}. Stopping.", page);
                yield break;
            }

            int count = 0;
            var idsOnPage = new HashSet<int>();
            foreach (var link in links)
            {
                var href = link.GetAttributeValue("href", "");
                var match = IdRegex.Match(href);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int id))
                {
                    if (idsOnPage.Add(id))
                    {
                        yield return id;
                        count++;
                    }
                }
            }
            
            _logger.LogInformation("Found {Count} IDs on page {Page}", count, page);

            var nextLink = doc.DocumentNode.SelectSingleNode("//a[@title='next page']");
            if (nextLink == null)
            {
                _logger.LogInformation("No next page link found. Discovery complete.");
                yield break;
            }

            page++;
            await Task.Delay(5000, ct); // Politeness delay
        }
    }
}
