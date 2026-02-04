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

    public async IAsyncEnumerable<int> DiscoverIdsByRankAsync(int startPage = 1, int? maxPagesOverride = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        int page = startPage;
        int maxPages = maxPagesOverride ?? _config.GetValue<int>("Scraper:RankMode:MaxPages", 10);

        _logger.LogInformation("Phase 1 - Scraping top ranked games (StartPage: {Start}, MaxPages: {Max})", startPage, maxPages);

        while (!ct.IsCancellationRequested)
        {
            if (page >= startPage + maxPages && maxPagesOverride.HasValue) // Logic: scrape 'maxPages' amount of pages relative to start
            {
                 // Actually the user semantics might be "scrape UNTIL page X". 
                 // Let's stick to "MaxPages means total pages to scrape in this run" or "Absolute max page index"?
                 // Standard browsing usually implies "scrape next N pages".
                 
                 // However, the original code treated MaxPages as an absolute limit (Config 10 -> stop at page 10).
                 // If I pass startPage=11, maxPages=10, I probably want pages 11-20.
                 // So let's check if we scraped enough.
            }

            // Simplest interpretation: scrape until page > (startPage + maxPages - 1) if maxPages is "count".
            // OR if maxPages is "absolute limit", then wait.
            
            // Let's treat maxPages as "number of pages to fetch in this request" if override is present.
            // If checking absolute limit from config, it's different.
            
            // Let's simplify: stop if we've processed 'maxPages' pages.
            if (page >= startPage + maxPages)
            {
                 _logger.LogInformation("Phase 1 - Reached max requested pages ({Max}). Stopping.", maxPages);
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
