using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using BoardGameScraper.Api.Models;

namespace BoardGameScraper.Api.Services;

/// <summary>
/// Service to scrape rulebook files from BoardGameGeek files section
/// BGG API doesn't provide file access, so we need to scrape HTML pages
/// </summary>
public class RulebookScraperService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RulebookScraperService> _logger;

    // Patterns to identify rulebook files
    private static readonly string[] RulebookKeywords = new[]
    {
        "rule", "rulebook", "manual", "instruction", "how to play", "gameplay",
        "regeln", "règles", "reglas", "regole", "règlement", "spelregels",
        "luật", "hướng dẫn", "quick start", "reference", "guide"
    };

    // File extensions to look for
    private static readonly string[] AllowedExtensions = new[] { ".pdf", ".doc", ".docx" };

    public RulebookScraperService(HttpClient httpClient, ILogger<RulebookScraperService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Scrape rulebook URLs for a specific game from BGG files section
    /// </summary>
    public async Task<List<RulebookInfo>> GetRulebooksForGameAsync(int bggId, CancellationToken ct = default)
    {
        var rulebooks = new List<RulebookInfo>();

        try
        {
            // BGG files page URL pattern - use the geekfilemodule API
            // This is a more reliable endpoint that returns file listings
            var filesUrl = $"https://api.geekdo.com/api/files?objectid={bggId}&objecttype=thing&nosession=1&showcount=50&pageid=1&sort=hot";

            _logger.LogDebug("Fetching files for BGG ID {Id}", bggId);

            var response = await _httpClient.GetAsync(filesUrl, ct);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                rulebooks.AddRange(ParseGeekdoApiResponse(json, bggId));
            }
            else
            {
                // Fallback: try HTML scraping of files page
                _logger.LogDebug("API failed, trying HTML scrape for BGG ID {Id}", bggId);
                rulebooks.AddRange(await ScrapeFilesPageHtmlAsync(bggId, ct));
            }

            _logger.LogInformation("Found {Count} rulebook(s) for BGG ID {Id}", rulebooks.Count, bggId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping rulebooks for BGG ID {Id}", bggId);
        }

        return rulebooks;
    }

    /// <summary>
    /// Parse the geekdo files API response
    /// </summary>
    private List<RulebookInfo> ParseGeekdoApiResponse(string json, int bggId)
    {
        var rulebooks = new List<RulebookInfo>();

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("files", out var files))
            {
                foreach (var file in files.EnumerateArray())
                {
                    var title = file.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var filename = file.TryGetProperty("filename", out var fn) ? fn.GetString() ?? "" : "";
                    var href = file.TryGetProperty("href", out var h) ? h.GetString() ?? "" : "";
                    var fileid = file.TryGetProperty("fileid", out var fid) ? fid.GetString() ?? "" : "";
                    var filepageid = file.TryGetProperty("filepageid", out var fpid) ? fpid.GetString() ?? "" : "";
                    var categoryid = file.TryGetProperty("categoryid", out var cid) ? cid.GetString() ?? "" : "";
                    var language = file.TryGetProperty("language", out var lang) ? lang.GetString() : null;

                    // Detect language from title/filename if not provided by API
                    var detectedLanguage = language ?? DetectLanguageFromText(title + " " + filename);

                    // FILTER: Only include English rulebooks
                    // Accept if: language is "English", or no language specified (could be English)
                    // Skip if language is explicitly set to another language
                    if (!string.IsNullOrEmpty(language) && !language.Equals("English", StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // Skip non-English files
                    }

                    // Also skip if we detected a non-English language from title/filename
                    if (detectedLanguage != null && !detectedLanguage.Equals("English", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Category 1 or 2 usually are rules/instructions
                    // Or check if title/filename contains rulebook keywords
                    if (IsRulebookFile(title, filename) || categoryid == "1" || categoryid == "2")
                    {
                        // Use the href from API response which provides the correct filepage URL
                        // Format: /filepage/{id}/{slug}
                        string fileUrl;
                        if (!string.IsNullOrEmpty(href))
                        {
                            fileUrl = $"https://boardgamegeek.com{href}";
                        }
                        else if (!string.IsNullOrEmpty(filepageid))
                        {
                            fileUrl = $"https://boardgamegeek.com/filepage/{filepageid}";
                        }
                        else
                        {
                            continue; // Skip if we can't construct a valid URL
                        }

                        rulebooks.Add(new RulebookInfo
                        {
                            Url = fileUrl,
                            Title = !string.IsNullOrEmpty(title) ? title : filename,
                            Language = "English", // We only keep English now
                            FileType = GetFileType(filename),
                            BggFileId = !string.IsNullOrEmpty(fileid) ? fileid : filepageid
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse geekdo API response for BGG ID {Id}", bggId);
        }

        return rulebooks;
    }


    /// <summary>
    /// Fallback: scrape the HTML files page
    /// </summary>
    private async Task<List<RulebookInfo>> ScrapeFilesPageHtmlAsync(int bggId, CancellationToken ct)
    {
        var rulebooks = new List<RulebookInfo>();

        try
        {
            var filesUrl = $"https://boardgamegeek.com/boardgame/{bggId}/files";
            var response = await _httpClient.GetAsync(filesUrl, ct);

            if (!response.IsSuccessStatusCode)
                return rulebooks;

            var html = await response.Content.ReadAsStringAsync(ct);

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            // Look for file links
            var fileLinks = htmlDoc.DocumentNode.SelectNodes("//a[contains(@href, '/filepage/')]");

            if (fileLinks != null)
            {
                foreach (var link in fileLinks)
                {
                    var href = link.GetAttributeValue("href", "");
                    var text = HttpUtility.HtmlDecode(link.InnerText?.Trim() ?? "");

                    if (IsRulebookFile(text, href))
                    {
                        // Extract file ID from filepage URL
                        var match = Regex.Match(href, @"/filepage/(\d+)");
                        if (match.Success)
                        {
                            var fileId = match.Groups[1].Value;
                            rulebooks.Add(new RulebookInfo
                            {
                                Url = $"https://boardgamegeek.com/filepage/{fileId}",
                                Title = text,
                                Language = DetectLanguageFromText(text),
                                FileType = "pdf" // Default assumption
                            });
                        }
                    }
                }
            }

            // Also check for direct PDF links
            var pdfLinks = htmlDoc.DocumentNode.SelectNodes("//a[contains(@href, '.pdf')]");
            if (pdfLinks != null)
            {
                foreach (var link in pdfLinks)
                {
                    var href = link.GetAttributeValue("href", "");
                    var text = HttpUtility.HtmlDecode(link.InnerText?.Trim() ?? "");

                    if (IsRulebookFile(text, href) && !rulebooks.Any(r => r.Url.Contains(href)))
                    {
                        rulebooks.Add(new RulebookInfo
                        {
                            Url = href.StartsWith("http") ? href : $"https://boardgamegeek.com{href}",
                            Title = text,
                            Language = DetectLanguageFromText(text),
                            FileType = "pdf"
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scrape HTML files page for BGG ID {Id}", bggId);
        }

        return rulebooks;
    }

    /// <summary>
    /// Batch process multiple games to get their rulebooks
    /// </summary>
    public async Task<Dictionary<int, List<RulebookInfo>>> GetRulebooksForGamesAsync(
        IEnumerable<int> bggIds,
        CancellationToken ct = default)
    {
        var results = new Dictionary<int, List<RulebookInfo>>();

        foreach (var id in bggIds)
        {
            if (ct.IsCancellationRequested)
                break;

            var rulebooks = await GetRulebooksForGameAsync(id, ct);
            results[id] = rulebooks;

            // Rate limiting - be respectful to BGG servers
            await Task.Delay(1500, ct);
        }

        return results;
    }

    private bool IsRulebookFile(string text, string href)
    {
        var lowerText = text.ToLowerInvariant();
        var lowerHref = href.ToLowerInvariant();

        // Check if filename or text contains rulebook keywords
        foreach (var keyword in RulebookKeywords)
        {
            if (lowerText.Contains(keyword) || lowerHref.Contains(keyword))
            {
                return true;
            }
        }

        // Also accept if it's a PDF with recognized patterns
        if (AllowedExtensions.Any(ext => lowerHref.EndsWith(ext)))
        {
            if (Regex.IsMatch(lowerHref, @"(rule|instruction|manual|guide|how.?to)", RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private string GetFileType(string filename)
    {
        var lower = filename.ToLowerInvariant();
        if (lower.EndsWith(".pdf"))
            return "pdf";
        if (lower.EndsWith(".doc"))
            return "doc";
        if (lower.EndsWith(".docx"))
            return "docx";
        return "unknown";
    }

    private string? DetectLanguageFromText(string text)
    {
        var lowerText = text.ToLowerInvariant();

        var languagePatterns = new Dictionary<string, string[]>
        {
            { "English", new[] { "english", " en ", "(en)", "_en_", "-en-", "_en." } },
            { "German", new[] { "german", "deutsch", " de ", "(de)", "_de_", "-de-", "_de." } },
            { "French", new[] { "french", "français", "francais", " fr ", "(fr)", "_fr_", "-fr-", "_fr." } },
            { "Spanish", new[] { "spanish", "español", "espanol", " es ", "(es)", "_es_", "-es-", "_es." } },
            { "Italian", new[] { "italian", "italiano", " it ", "(it)", "_it_", "-it-", "_it." } },
            { "Dutch", new[] { "dutch", "nederlands", " nl ", "(nl)", "_nl_", "-nl-", "_nl." } },
            { "Polish", new[] { "polish", "polski", " pl ", "(pl)", "_pl_", "-pl-", "_pl." } },
            { "Portuguese", new[] { "portuguese", "português", "portugues", " pt ", "(pt)", "_pt_", "-pt-", "_pt." } },
            { "Japanese", new[] { "japanese", "日本語", " jp ", "(jp)", "_jp_", "-jp-", "_jp." } },
            { "Chinese", new[] { "chinese", "中文", " zh ", "(zh)", "_zh_", "-zh-", "_zh." } },
            { "Korean", new[] { "korean", "한국어", " ko ", "(ko)", "_ko_", "-ko-", "_ko." } },
            { "Vietnamese", new[] { "vietnamese", "tiếng việt", "viet", " vi ", "(vi)", "_vi_", "-vi-", "_vi." } },
            { "Russian", new[] { "russian", "русский", " ru ", "(ru)", "_ru_", "-ru-", "_ru." } },
            { "Czech", new[] { "czech", "čeština", " cz ", "(cz)", "_cz_", "-cz-", "_cz." } },
            { "Hungarian", new[] { "hungarian", "magyar", " hu ", "(hu)", "_hu_", "-hu-", "_hu." } }
        };

        foreach (var (lang, patterns) in languagePatterns)
        {
            foreach (var pattern in patterns)
            {
                if (lowerText.Contains(pattern))
                {
                    return lang;
                }
            }
        }

        return null;
    }
}
