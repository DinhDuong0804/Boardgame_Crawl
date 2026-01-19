using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace BoardGameScraper.Api.Services;

public class BggDiscoveryService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BggDiscoveryService> _logger;
    private const string BaseUrl = "https://boardgamegeek.com/browse/boardgame/page/";
    private static readonly Regex IdRegex = new(@"/boardgame/(\d+)/", RegexOptions.Compiled);

    private readonly IConfiguration _config;

    public BggDiscoveryService(HttpClient httpClient, ILogger<BggDiscoveryService> logger, IConfiguration config)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config;
    }

    public async IAsyncEnumerable<int> DiscoverIdsByRankAsync(int startPage = 1, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        int page = startPage;
        int maxPages = _config.GetValue<int>("Scraper:RankMode:MaxPages", 10); // Default 10 pages (~1000 items)

        _logger.LogInformation("Phase 1 - Scraping top ranked games (MaxPages: {MaxPages})", maxPages);

        while (!ct.IsCancellationRequested)
        {
            if (page > maxPages)
            {
                 _logger.LogInformation("Phase 1 - Reached max configured pages ({Max}). Moving to Phase 2.", maxPages);
                 yield break;
            }

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
                await Task.Delay(5000, ct);
                continue; 
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var links = doc.DocumentNode.SelectNodes("//a[contains(@href, '/boardgame/')]");
            
            if (links == null || links.Count == 0)
            {
                _logger.LogWarning("No links found on page {Page}. Stopping Phase 1.", page);
                try 
                {
                    await System.IO.File.WriteAllTextAsync($"failed_page_{page}.html", html, ct);
                }
                catch { }
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
                _logger.LogInformation("No next page link found. Discovery Phase 1 complete.");
                yield break;
            }

            page++;
            await Task.Delay(5000, ct); // Politeness delay
        }
    }

    public async IAsyncEnumerable<int> DiscoverIdsBySequenceAsync(int? resumeId = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        int startId = resumeId ?? _config.GetValue<int>("Scraper:IdSequence:StartId", 1);
        int endId = _config.GetValue<int>("Scraper:IdSequence:EndId", 100000);

        _logger.LogInformation("Phase 2 - Generating IDs from {StartId} to {EndId}", startId, endId);

        for (int id = startId; id <= endId; id++)
        {
            if (ct.IsCancellationRequested) yield break;
            yield return id;
        }
    }

}
