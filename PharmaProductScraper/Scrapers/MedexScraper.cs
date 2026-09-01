using System.Net;
using HtmlAgilityPack;
using PharmaProductScraper.Models;

namespace PharmaProductScraper.Scrapers;

public sealed class MedexScraper
{
    private const string SearchUrl = "https://medex.com.bd/search";
    private readonly HttpClient _httpClient;

    public MedexScraper(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ScrapedProduct?> SearchAsync(string productName, CancellationToken ct = default)
    {
        var url = $"{SearchUrl}?search={Uri.EscapeDataString(productName)}";
        var html = await _httpClient.GetStringAsync(url, ct);
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var candidates = document.DocumentNode.SelectNodes("//a[contains(@href,'/brands/')]");

        if (candidates is null)
            return null;

        var match = candidates
            .Select(x => new
            {
                Node = x,
                Name = WebUtility.HtmlDecode(x.InnerText).Trim(),
                Url = x.GetAttributeValue("href", string.Empty)
            })
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Name) &&
                !string.IsNullOrWhiteSpace(x.Url))
            .OrderByDescending(x => GetMatchScore(productName, x.Name))
            .FirstOrDefault();

        if (match is null)
            return null;

        return await GetDetailsAsync(
            match.Url,
            ct);
    }

    private async Task<ScrapedProduct?> GetDetailsAsync(
        string url,
        CancellationToken ct)
    {
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "https://medex.com.bd" + url;

        var html = await _httpClient.GetStringAsync(url, ct);

        var document = new HtmlDocument();

        document.LoadHtml(html);

        var result = new ScrapedProduct
        {
            Source = "MedEx",
            ProductUrl = url
        };

        ParseTitle(document, result);
        ParseImage(document, result);
        ParsePrice(document, result);
        ParseMonograph(document, result);

        result.ExternalId = GetBrandId(url);

        return result;
    }

    private static void ParseTitle(HtmlDocument document, ScrapedProduct result)
    {
        var title = document.DocumentNode.SelectSingleNode("//title")?.InnerText;

        if (string.IsNullOrWhiteSpace(title))
            return;

        title = WebUtility.HtmlDecode(title);

        var parts = title.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 0)
            result.Name = parts[0];

        if (parts.Length > 1)
            result.Strength = parts[1];

        if (parts.Length > 2)
            result.Type = parts[2];

        if (parts.Length > 4)
            result.Manufacturer = parts[4];
    }

    private static void ParseImage(HtmlDocument document, ScrapedProduct result)
    {
        var image = document.DocumentNode.SelectSingleNode("//img[contains(@src,'packaging')]");

        if (image is null)
            return;

        result.ImageUrl = image.GetAttributeValue("src", null) ?? image.GetAttributeValue("data-src", null);
    }

    private static void ParsePrice(HtmlDocument document, ScrapedProduct result)
    {
        var text = WebUtility.HtmlDecode(document.DocumentNode.InnerText);

        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

        var match = System.Text.RegularExpressions.Regex.Match(text, @"Unit Price\s*:\s*৳\s*([\d,.]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success)
            return;

        var value = match.Groups[1].Value.Replace(",", string.Empty);

        if (double.TryParse(value, out var price))
            result.Price = price;
    }

    private static void ParseMonograph(HtmlDocument document, ScrapedProduct result)
    {
        var mapping =
            new Dictionary<string, string>
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

            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

            if (!string.IsNullOrWhiteSpace(text))
                result.Monograph[item.Value] = text;
        }
    }

    private static string? GetBrandId(string url)
    {
        var match = System.Text.RegularExpressions.Regex.Match(url, @"/brands/(\d+)/");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static int GetMatchScore(string search, string result)
    {
        var a = Normalize(search);
        var b = Normalize(result);

        if (a == b)
            return 100;

        if (b.StartsWith(a))
            return 80;

        if (b.Contains(a))
            return 60;

        return 0;
    }

    private static string Normalize(string value)
    {
        return string.Join(" ", value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}