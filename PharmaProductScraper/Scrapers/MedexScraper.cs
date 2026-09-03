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
        List<ScrapedProduct> matches = new();

        // 1. Search MedEx only once by product name.
        if (!string.IsNullOrWhiteSpace(name))
        {
            var candidates = await FetchCandidatesAsync(name, ct);

            //matches = FindBestMatch(candidates, name, null, null, null);
            matches = candidates;

            //if (matches.Count == 0)
            //    return null;
        }
        else
        {
            return null;
        }

        // 2. Filter same candidate list by strength.
        if (!string.IsNullOrWhiteSpace(strength))
        {
            matches = FindBestMatch(matches, null, strength, null, null);

            if (matches.Count == 0)
                return null;
        }

        // 3. Filter same candidate list by form.
        if (!string.IsNullOrWhiteSpace(form))
        {
            matches = FindBestMatch(matches, null, null, form, null);

            if (matches.Count == 0)
                return null;
        }

        // 4. Filter same candidate list by generic name.
        if (!string.IsNullOrWhiteSpace(genericName))
        {
            matches = FindBestMatch(matches, null, null, null, genericName);

            if (matches.Count == 0)
                return null;
        }

        return matches.FirstOrDefault();
    }

    private async Task<List<ScrapedProduct>> FetchCandidatesAsync(
        string query,
        CancellationToken ct)
    {
        try
        {
            var url =
                $"{SearchUrl}?search={Uri.EscapeDataString(query)}";

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
                {
                    continue;
                }

                if (!productUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    productUrl = BaseUrl + productUrl;
                }

                // MedEx sometimes has duplicate brand anchors.
                if (!processedUrls.Add(productUrl))
                    continue;

                var genericName = TryGetGenericFromSearchNode(node);

                var candidate = await GetDetailsAsync(productUrl, genericName, ct);

                if (candidate is null)
                    continue;

                // Search page name is usually cleaner than title-derived name.
                if (string.IsNullOrWhiteSpace(candidate.Name))
                    candidate.Name = productName;

                if (string.IsNullOrWhiteSpace(candidate.GenericName))
                    candidate.GenericName = genericName;

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
            {
                url = BaseUrl + url;
            }

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

    private static List<ScrapedProduct> FindBestMatch(
        List<ScrapedProduct> candidates,
        string? targetName,
        string? targetStrength,
        string? targetForm,
        string? targetGenericName,
        int score = 0)
    {
        if (candidates.Count == 0)
            return new List<ScrapedProduct>();

        var scoredList = candidates
            .Select(candidate => new
            {
                Product = candidate,
                Score = CalculateScore(candidate, targetName, targetStrength, targetForm, targetGenericName, score)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToList();

        return scoredList
            .Select(x => x.Product)
            .ToList();
    }

    private static int CalculateScore(
        ScrapedProduct candidate,
        string? targetName,
        string? targetStrength,
        string? targetForm,
        string? targetGenericName,
        int score = 0)
    {
        if (!string.IsNullOrWhiteSpace(targetName))
            score += GetMatchScore(targetName, candidate.Name);

        if (!string.IsNullOrWhiteSpace(targetStrength))
            score += GetMatchScore(targetStrength, candidate.Strength);

        if (!string.IsNullOrWhiteSpace(targetForm))
            score += GetMatchScore(targetForm, candidate.Type);

        if (!string.IsNullOrWhiteSpace(targetGenericName))
            score += GetMatchScore(targetGenericName, candidate.GenericName);

        return score;
    }

    private static void ParseTitle(
    HtmlDocument document,
    ScrapedProduct result)
    {
        var title = document.DocumentNode
            .SelectSingleNode("//title")
            ?.InnerText;

        if (string.IsNullOrWhiteSpace(title))
            return;

        title = WebUtility.HtmlDecode(title);

        var parts = title.Split(
            '|',
            StringSplitOptions.TrimEntries |
            StringSplitOptions.RemoveEmptyEntries);

        // MedEx title typically:
        //
        // Napa | 500 mg | Tablet | নাপা |
        // Beximco Pharmaceuticals Ltd. | ...

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
        {
            // Fallback to Strip Price.
            match = Regex.Match(text, @"Strip Price\s*:\s*৳\s*([\d,.]+)", RegexOptions.IgnoreCase);
        }

        if (!match.Success)
            return;

        var value = match.Groups[1].Value.Replace(",", string.Empty);

        if (double.TryParse(value, out var price))
        {
            result.Price = price;
        }
    }

    private static void ParsePackSize(
        HtmlDocument document,
        ScrapedProduct result)
    {
        var node = document.DocumentNode
            .SelectSingleNode("//*[contains(@class,'pack-size-info')]");

        if (node is null)
            return;

        var text = WebUtility.HtmlDecode(node.InnerText);

        text = Regex.Replace(
            text,
            @"\s+",
            " ").Trim();

        var match = Regex.Match(
            text,
            @"(\d+(?:\.\d+)?)\s*x\s*(\d+(?:\.\d+)?)",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return;

        var firstValue = match.Groups[1].Value.Replace(",", string.Empty);
        var secondValue = match.Groups[2].Value.Replace(",", string.Empty);

        if (!int.TryParse(firstValue, out var stripCount) ||
            !int.TryParse(secondValue, out var unitsPerStrip))
        {
            return;
        }

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

    private static int GetMatchScore(string search, string? result)
    {
        if (string.IsNullOrWhiteSpace(result) || string.IsNullOrWhiteSpace(search))
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