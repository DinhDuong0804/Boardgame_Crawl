using System.Text.RegularExpressions;

namespace BoardGameScraper.Api.Services;

/// <summary>
/// Service to download PDF rulebooks from BoardGameGeek
/// </summary>
public class BggPdfDownloadService
{
    private readonly ILogger<BggPdfDownloadService> _logger;
    private readonly HttpClient _httpClient;

    private readonly IConfiguration _config;
    private readonly BggPlaywrightService _playwrightService;
    private bool _isLoggedIn = false;

    public BggPdfDownloadService(
        ILogger<BggPdfDownloadService> logger,
        HttpClient httpClient,
        IConfiguration config,
        BggPlaywrightService playwrightService)
    {
        _logger = logger;
        _httpClient = httpClient;
        _config = config;
        _playwrightService = playwrightService;
    }

    /// <summary>
    /// Download PDF from BGG
    /// </summary>
    /// <param name="url">BGG file URL</param>
    /// <param name="bggFileId">Optional BGG file ID</param>
    /// <returns>PDF content as byte array</returns>
    public async Task<byte[]> DownloadPdfAsync(string url, string? bggFileId = null)
    {
        _logger.LogInformation($"Downloading PDF from: {url} (BggFileId: {bggFileId ?? "none"})");

        try
        {
            // NEW STRATEGY: Try Playwright first for maximum stability
            _logger.LogInformation("Attempting download via Playwright...");
            var pwBytes = await _playwrightService.DownloadPdfAsync(url, bggFileId);
            if (pwBytes != null && pwBytes.Length > 0)
            {
                return pwBytes;
            }
            _logger.LogWarning("Playwright download returned no data. Falling back to HttpClient...");

            // Ensure logged in if credentials available (for fallback)
            await EnsureLoggedInAsync();

            HttpResponseMessage? response = null;

            // Strategy 1: If we have a BGG File ID, try direct download patterns first
            if (!string.IsNullOrEmpty(bggFileId))
            {
                var directUrlPatterns = new[]
                {
                    $"https://boardgamegeek.com/file/download/{bggFileId}",
                    $"https://boardgamegeek.com/filepage/download/{bggFileId}",
                    $"https://boardgamegeek.com/file/download_redirect/{bggFileId}"
                };

                foreach (var directUrl in directUrlPatterns)
                {
                    try
                    {
                        _logger.LogInformation($"Trying direct download: {directUrl}");
                        var resp = await _httpClient.GetAsync(directUrl);
                        if (resp.IsSuccessStatusCode && resp.Content.Headers.ContentType?.MediaType?.Contains("pdf") == true)
                        {
                            response = resp;
                            _logger.LogInformation($"Direct download success: {directUrl}");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"Direct download failed for {directUrl}: {ex.Message}");
                    }
                }
            }

            // Strategy 2: If Strategy 1 failed or no ID, try the provided URL
            if (response == null)
            {
                response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                // Check content type
                var contentType = response.Content.Headers.ContentType?.MediaType;
                _logger.LogInformation($"Initial response Content-Type: {contentType}");

                // If it's a BGG file page (HTML), we need to extract the actual PDF URL
                if (contentType?.Contains("text/html") == true)
                {
                    _logger.LogInformation("Got HTML page, extracting PDF URL...");
                    var html = await response.Content.ReadAsStringAsync();
                    var pdfUrl = ExtractPdfUrlFromHtml(html);
                    
                    if (string.IsNullOrEmpty(pdfUrl))
                    {
                        // Last ditch effort: if we didn't have an ID but the URL has one, try to extract it and use direct link
                        var idMatch = Regex.Match(url, @"/filepage/(\d+)");
                        if (idMatch.Success && string.IsNullOrEmpty(bggFileId))
                        {
                            var extractedId = idMatch.Groups[1].Value;
                            _logger.LogInformation($"Extracted ID {extractedId} from URL, trying direct download...");
                            var directUrl = $"https://boardgamegeek.com/file/download/{extractedId}";
                            var resp = await _httpClient.GetAsync(directUrl);
                            if (resp.IsSuccessStatusCode && resp.Content.Headers.ContentType?.MediaType?.Contains("pdf") == true)
                            {
                                response = resp;
                            }
                        }
                        
                        if (response == null || response.Content.Headers.ContentType?.MediaType?.Contains("text/html") == true)
                        {
                            throw new InvalidOperationException("Could not extract PDF URL from BGG page and direct download failed.");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"Found PDF URL: {pdfUrl}");
                        
                        // Download actual PDF
                        response = await _httpClient.GetAsync(pdfUrl);
                        response.EnsureSuccessStatusCode();
                    }
                }
            }

            var pdfBytes = await response.Content.ReadAsByteArrayAsync();
            _logger.LogInformation($"Downloaded {pdfBytes.Length} bytes");

            return pdfBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error downloading PDF from {url}");
            throw new InvalidOperationException($"Failed to download PDF from BGG: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extract actual PDF URL from BGG file page HTML
    /// </summary>
    private string? ExtractPdfUrlFromHtml(string html)
    {
        try
        {
            // Pattern 1: Direct link to geekdo files or specific redirect paths
            var directPatterns = new[]
            {
                @"href=[""'](https?://cf\.geekdo-files\.com/[^""']+\.pdf)[""']",
                @"href=[""'](https?://cf\.geekdo-images\.com/[^""']+\.pdf)[""']",
                @"href=[""'](https?://[^""']+geekdo[^""']+\.pdf)[""']",
                @"href=[""'](/file/download_redirect/[^""']+)[""']",
                @"href=[""'](/file/download/[^""']+)[""']",
                @"href=[""'](/filepage/download/[^""']+)[""']",
                @"href=[""'](/filepage/download_redirect/[^""']+)[""']"
            };

            foreach (var pattern in directPatterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(html, pattern, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (match.Success)
                {
                    var url = match.Groups[1].Value;
                    if (url.StartsWith("/"))
                    {
                        url = "https://boardgamegeek.com" + url;
                    }
                    _logger.LogInformation($"Found PDF URL via direct pattern: {url}");
                    return url;
                }
            }

            // Pattern 2: Search for any link containing /file/download or /file/download_redirect
            var genericDownloadPattern = @"href=[""'](/file(?:page)?/(?:download|download_redirect)/[^""']+)[""']";
            var genericMatch = System.Text.RegularExpressions.Regex.Match(html, genericDownloadPattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (genericMatch.Success)
            {
                var url = "https://boardgamegeek.com" + genericMatch.Groups[1].Value;
                _logger.LogInformation($"Found PDF URL via generic download pattern: {url}");
                return url;
            }

            // Pattern 3: Look for anchor tags that contain the word "Download"
            var downloadLinkPattern = @"<a[^>]+href=[""']([^""']+)[""'][^>]*>.*?download.*?</a>";
            var downloadMatch = System.Text.RegularExpressions.Regex.Match(html, downloadLinkPattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

            if (downloadMatch.Success)
            {
                var url = downloadMatch.Groups[1].Value;
                if (url.StartsWith("/"))
                {
                    url = "https://boardgamegeek.com" + url;
                }
                _logger.LogInformation($"Found PDF URL via 'Download' text pattern: {url}");
                return url;
            }

            _logger.LogWarning("PDF URL not found in BGG page HTML. HTML snippet (1000 chars): " + 
                (html.Length > 1000 ? html.Substring(0, 1000) : html));

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting PDF URL from HTML");
            return null;
        }
    }

    /// <summary>
    /// Ensure the client is logged into BGG
    /// </summary>
    private async Task EnsureLoggedInAsync()
    {
        if (_isLoggedIn) return;

        var username = _config["BggApi:Username"];
        var password = _config["BggApi:Password"];

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            _logger.LogWarning("BGG Credentials not found in configuration. Proceeding as guest.");
            return;
        }

        try
        {
            _logger.LogInformation($"Attempting to login to BGG as {username}...");

            // BGG uses a login post to /login
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", password)
            });

            // Add some headers common for forms
            var loginRequest = new HttpRequestMessage(HttpMethod.Post, "https://boardgamegeek.com/login")
            {
                Content = content
            };
            loginRequest.Headers.Referrer = new Uri("https://boardgamegeek.com/login");

            var response = await _httpClient.SendAsync(loginRequest);
            
            // BGG usually redirects or returns OK with session cookies
            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Redirect)
            {
                _logger.LogInformation("BGG login request sent successfully.");
                _isLoggedIn = true;
            }
            else
            {
                _logger.LogWarning($"BGG login failed with status code: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during BGG login");
        }
    }

    /// <summary>
    /// Check if URL is a valid BGG URL
    /// </summary>
    public bool IsValidBggUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var uri = new Uri(url, UriKind.RelativeOrAbsolute);
        
        if (!uri.IsAbsoluteUri)
            return false;

        var host = uri.Host.ToLower();
        return host.Contains("boardgamegeek.com") || 
               host.Contains("geekdo-files.com") ||
               host.Contains("geekdo-images.com");
    }
}
