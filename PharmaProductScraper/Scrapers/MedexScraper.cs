using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using PharmaProductScraper.Models;

namespace PharmaProductScraper.Scrapers;

public sealed class MedexScraper
{
    private const string SearchUrl = "https://medex.com.bd/search";
    private const string BaseUrl = "https://medex.com.bd";

    private readonly HttpClient _httpClient;

    public MedexScraper(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ScrapedProduct?> SearchAsync(Product product, CancellationToken ct = default)
    {
        return await SearchAsync(
            product.Name,
            product.Strength,
            product.Form ?? product.Type,
            product.GenericName,
            ct);
    }

    public async Task<ScrapedProduct?> SearchAsync(
        string? name,
        string? strength = null,
        string? form = null,
        string? genericName = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        // 1. Search MedEx only once by product name.
        var candidates = await FetchCandidatesAsync(name, ct);

        if (candidates.Count == 0)
            return null;

        // 2. Score by Name + Strength + Form.
        // 3. Keep original MedEx ordering when scores are equal.
        // 4. Starting from highest score, find the first GenericName match.
        var selectedCandidate = FindBestMatch(candidates, name, strength, form, genericName);

        if (selectedCandidate is null || string.IsNullOrWhiteSpace(selectedCandidate.ProductUrl))
            return null;

        // Fetch only the selected product detail page.
        return await GetDetailsAsync(selectedCandidate.ProductUrl, selectedCandidate.GenericName, ct);
    }

    private async Task<List<ScrapedProduct>> FetchCandidatesAsync(
        string query,
        CancellationToken ct)
    {
        try
        {
            var url = $"{SearchUrl}?search={Uri.EscapeDataString(query)}";
            var html = await _httpClient.GetStringAsync(url, ct);
            var document = new HtmlDocument();
            document.LoadHtml(html);
            var nodes = document.DocumentNode.SelectNodes("//a[contains(@href,'/brands/')]");

            if (nodes is null)
                return new List<ScrapedProduct>();

            var candidates = new List<ScrapedProduct>();
            var processedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in nodes)
            {
                var productName = WebUtility.HtmlDecode(node.InnerText).Trim();
                var productUrl = node.GetAttributeValue("href", string.Empty);

                if (string.IsNullOrWhiteSpace(productName) || string.IsNullOrWhiteSpace(productUrl))
                    continue;

                if (!productUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    productUrl = BaseUrl + productUrl;

                // MedEx sometimes has duplicate brand anchors.
                if (!processedUrls.Add(productUrl))
                    continue;

                var candidate = new ScrapedProduct
                {
                    Source = "MedEx",
                    Name = productName,
                    GenericName = TryGetGenericFromSearchNode(node),
                    ProductUrl = productUrl,
                    ExternalId = GetBrandId(productUrl)
                };

                // Example:
                // Napa 500 mg (Suppository)
                //
                // becomes:
                // Name     = Napa
                // Strength = 500 mg
                // Type     = Suppository
                ParseSearchResultName(candidate);

                candidates.Add(candidate);
            }

            return candidates;
        }
        catch
        {
            return new List<ScrapedProduct>();
        }
    }

    private async Task<ScrapedProduct?> GetDetailsAsync(string url, string? genericName, CancellationToken ct)
    {
        try
        {
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = BaseUrl + url;

            var html = await _httpClient.GetStringAsync(url, ct);
            var document = new HtmlDocument();
            document.LoadHtml(html);

            var result = new ScrapedProduct
            {
                Source = "MedEx",
                ProductUrl = url,
                GenericName = genericName,
                ExternalId = GetBrandId(url)
            };

            ParseTitle(document, result);
            ParseImage(document, result);
            ParsePrice(document, result);
            ParsePackSize(document, result);
            ParseMonograph(document, result);

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static ScrapedProduct? FindBestMatch(
        List<ScrapedProduct> candidates,
        string? targetName,
        string? targetStrength,
        string? targetForm,
        string? targetGenericName)
    {
        if (candidates.Count == 0)
            return null;

        var scoredCandidates = candidates
            .Select((candidate, index) => new
            {
                Product = candidate,
                OriginalIndex = index,
                Score = GetMatchScore(targetName, candidate.Name) +
                        GetMatchScore(targetStrength, candidate.Strength) +
                        GetMatchScore(targetForm, candidate.Type)
            })
            .Where(x => x.Score > 1)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.OriginalIndex)
            .ToList();

        if (scoredCandidates.Count == 0)
            return null;

        var maxScore = scoredCandidates[0].Score;

        var maxScoredCandidates = scoredCandidates
            .Where(x => x.Score == maxScore)
            .ToList();

        if (!string.IsNullOrWhiteSpace(targetGenericName))
        {
            //foreach (var candidate in scoredCandidates)
            foreach (var candidate in maxScoredCandidates)
            {
                if (GetMatchScore(targetGenericName, candidate.Product.GenericName) > 0)
                    return candidate.Product;
            }

            return null;
        }

        return maxScoredCandidates.First().Product;
    }

    private static void ParseSearchResultName(ScrapedProduct candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Name))
            return;

        var value = candidate.Name.Trim();

        // Example:
        // Napa 500 mg (Suppository)
        //
        // Extract:
        // Type = Suppository
        var formMatch = Regex.Match(value, @"\(([^)]+)\)\s*$");

        if (formMatch.Success)
        {
            candidate.Type = formMatch.Groups[1].Value.Trim();
            candidate.Category = candidate.Type;
            value = value.Substring(0, formMatch.Index).Trim();
        }

        // Examples:
        // Napa 500 mg
        // Napa Extra 500 mg+65 mg
        // Napa Extend 665 mg
        //
        // Extract strength from the end.
        var strengthMatch = Regex.Match(
            value,
            @"(\d+(?:\.\d+)?\s*(?:mg|mcg|g|kg|ml|l|iu|unit|units|%)(?:\s*\+\s*\d+(?:\.\d+)?\s*(?:mg|mcg|g|kg|ml|l|iu|unit|units|%))*)\s*$",
            RegexOptions.IgnoreCase);

        if (strengthMatch.Success)
        {
            candidate.Strength = strengthMatch.Groups[1].Value.Trim();
            candidate.Name = value.Substring(0, strengthMatch.Index).Trim();
        }
        else
        {
            candidate.Name = value;
        }
    }

    private static void ParseTitle(HtmlDocument document, ScrapedProduct result)
    {
        var title = document.DocumentNode.SelectSingleNode("//title")?.InnerText;

        if (string.IsNullOrWhiteSpace(title))
            return;

        title = WebUtility.HtmlDecode(title);

        var parts = title.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // MedEx title normally:
        // Napa | 500 mg | Tablet | নাপা | Beximco Pharmaceuticals Ltd. | ...

        if (parts.Length > 0)
            result.Name = parts[0];

        if (parts.Length > 1)
            result.Strength = parts[1];

        if (parts.Length > 2)
        {
            result.Type = parts[2];
            result.Category = parts[2];
        }

        // parts[3] is usually Bangla brand name,
        // therefore do NOT assign it to GenericName.

        if (parts.Length > 4)
            result.Manufacturer = parts[4];
    }

    private static string? TryGetGenericFromSearchNode(
        HtmlNode brandNode)
    {
        try
        {
            // MedEx search results normally contain the generic
            // shortly after the brand link inside an <i> element.
            var italicNode = brandNode.SelectSingleNode("following::i[1]");

            if (italicNode is null)
                return null;

            var value = WebUtility.HtmlDecode(italicNode.InnerText).Trim();

            if (string.IsNullOrWhiteSpace(value))
                return null;

            // Example:
            // (Paracetamol)
            //
            // becomes:
            // Paracetamol
            return value.Trim().Trim('(', ')').Trim();
        }
        catch
        {
            return null;
        }
    }

    private static void ParseImage(HtmlDocument document, ScrapedProduct result)
    {
        var image = document.DocumentNode.SelectSingleNode("//img[contains(@src,'packaging') or contains(@data-src,'packaging')]");

        if (image is null)
            return;

        result.ImageUrl = image.GetAttributeValue("src", null) ?? image.GetAttributeValue("data-src", null);
    }

    private static void ParsePrice(HtmlDocument document, ScrapedProduct result)
    {
        var text = WebUtility.HtmlDecode(document.DocumentNode.InnerText);

        text = Regex.Replace(text, @"\s+", " ");

        // Prefer Unit Price.
        var match = Regex.Match(text, @"Unit Price\s*:\s*৳\s*([\d,.]+)", RegexOptions.IgnoreCase);

        if (!match.Success)
            match = Regex.Match(text, @"Strip Price\s*:\s*৳\s*([\d,.]+)", RegexOptions.IgnoreCase);  // Fallback to Strip Price.

        if (!match.Success)
            return;

        var value = match.Groups[1].Value.Replace(",", string.Empty);

        if (double.TryParse(value, out var price))
            result.Price = price;
    }

    private static void ParsePackSize(HtmlDocument document, ScrapedProduct result)
    {
        var node = document.DocumentNode.SelectSingleNode("//*[contains(@class,'pack-size-info')]");

        if (node is null)
            return;

        var text = WebUtility.HtmlDecode(node.InnerText);
        text = Regex.Replace(text, @"\s+", " ").Trim();

        var match = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*x\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);

        if (!match.Success)
            return;

        var firstValue = match.Groups[1].Value.Replace(",", string.Empty);
        var secondValue = match.Groups[2].Value.Replace(",", string.Empty);

        if (!int.TryParse(firstValue, out var stripCount) || !int.TryParse(secondValue, out var unitsPerStrip))
            return;

        result.PackSize = unitsPerStrip;
        result.MedicinePerStrips = unitsPerStrip;
        result.Size = $"{stripCount} x {unitsPerStrip}";
    }

    private static void ParseMonograph(HtmlDocument document, ScrapedProduct result)
    {
        var mapping = new Dictionary<string, string>
        {
            ["indications"] = "indication",
            ["mode_of_action"] = "pharmacology",
            ["dosage"] = "dosage",
            ["interaction"] = "interaction",
            ["contraindications"] = "contraindication",
            ["side_effects"] = "side_effect",
            ["pregnancy_cat"] = "pregnancy_lactation",
            ["precautions"] = "precaution",
            ["pediatric_uses"] = "special_populations",
            ["overdose_effects"] = "overdose",
            ["drug_classes"] = "therapeutic_class",
            ["storage_conditions"] = "storage"
        };

        foreach (var item in mapping)
        {
            var node = document.GetElementbyId(item.Key);

            if (node is null)
                continue;

            var text = WebUtility.HtmlDecode(node.InnerText);

            text = Regex.Replace(text, @"\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(text))
                continue;

            result.Monograph[item.Value] = text;
        }
    }

    private static string? GetBrandId(string url)
    {
        var match = Regex.Match(url, @"/brands/(\d+)/", RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value : null;
    }

    private static int GetMatchScore(string? search, string? result)
    {
        if (string.IsNullOrWhiteSpace(search) || string.IsNullOrWhiteSpace(result))
            return 0;

        var a = Normalize(search);
        var b = Normalize(result);

        if (a == b)
            return 1;

        if (b.StartsWith(a) || a.StartsWith(b))
            return 1;

        if (b.Contains(a) || a.Contains(b))
            return 1;

        return 0;
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value.Trim().ToLowerInvariant();

        // Multiple spaces/tabs/newlines -> one space.
        value = Regex.Replace(value, @"\s+", " ");

        // Normalize medicine strengths:
        // 500 mg -> 500mg || 500MG -> 500mg || 500/mg -> 500mg || 500-Mg -> 500mg || 500_mg -> 500mg || 10 / ml -> 10ml
        value = Regex.Replace(value, @"(\d+(?:\.\d+)?)\s*[/\-_]?\s*(mg|mcg|g|kg|ml|l|iu|unit|units|%)\b", "$1$2");

        return value;
    }
}