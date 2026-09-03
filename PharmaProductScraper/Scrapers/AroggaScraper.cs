using PharmaProductScraper.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PharmaProductScraper.Scrapers;

public sealed class AroggaScraper
{
    private const string SearchUrl = "https://api.arogga.com/general/v3/search";
    private const string ProductUrl = "https://www.arogga.com/product/{0}";

    private readonly HttpClient _httpClient;

    public AroggaScraper(HttpClient httpClient)
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

    //public async Task<ScrapedProduct?> SearchAsync(string productName, CancellationToken ct = default)
    //{
    //    return await SearchAsync(productName, null, null, null, ct);
    //}

    public async Task<ScrapedProduct?> SearchAsync(
        string? name,
        string? strength = null,
        string? form = null,
        string? genericName = null,
        CancellationToken ct = default)
    {
        var match = new List<ScrapedProduct?>();
        // 1. First search with name
        if (!string.IsNullOrWhiteSpace(name))
        {
            var candidates = await FetchCandidatesAsync(name, ct);
            match = FindBestMatch(candidates, name, null, null, null);
            //if (match is not null)
            //return match;
            if (match is null)
                return null;
        }

        // 2. Then search with strength
        if (!string.IsNullOrWhiteSpace(strength))
        {
            //var candidates = await FetchCandidatesAsync(strength, ct);
            match = FindBestMatch(match, null, strength, null, null);
            //if (match is not null)
            //    return match;
            if (match is null)
                return null;
        }

        // 3. Then search with p_form
        if (!string.IsNullOrWhiteSpace(form))
        {
            //var candidates = await FetchCandidatesAsync(form, ct);
            match = FindBestMatch(match, null, null, form, null);
            if (match is null)
                return null;
        }

        // 4. Then search with p_generic_name
        if (!string.IsNullOrWhiteSpace(genericName))
        {
            //var candidates = await FetchCandidatesAsync(genericName, ct);
            match = FindBestMatch(match, null, null, null, genericName);
            if (match is null)
                return null;
        }

        return match.FirstOrDefault();
    }

    private async Task<List<ScrapedProduct>> FetchCandidatesAsync(string query, CancellationToken ct)
    {
        try
        {
            var url =
                $"{SearchUrl}" +
                $"?_type=web" +
                $"&_page=1" +
                $"&_perPage=20" +
                $"&_search={Uri.EscapeDataString(query)}";

            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return new List<ScrapedProduct>();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
                return new List<ScrapedProduct>();

            var candidates = new List<ScrapedProduct>();

            foreach (var item in data.EnumerateArray())
            {
                var id = GetStringOrNumber(item, "p_id");
                var name = GetString(item, "p_name");
                var generic = GetString(item, "p_generic_name");
                var strength = GetString(item, "p_strength");
                var form = GetString(item, "p_form");

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                candidates.Add(new ScrapedProduct
                {
                    Source = "Arogga",
                    ExternalId = id,
                    Name = name,
                    GenericName = generic,
                    Strength = strength,
                    Type = form,

                    ProductUrl = string.IsNullOrWhiteSpace(id) ? null : string.Format(ProductUrl, id)
                });
            }

            return candidates;
        }
        catch
        {
            return new List<ScrapedProduct>();
        }
    }

    private static List<ScrapedProduct?> FindBestMatch(
        List<ScrapedProduct> candidates,
        string? targetName,
        string? targetStrength,
        string? targetForm,
        string? targetGenericName,
        int score = 0)
    {
        if (candidates.Count == 0)
            return null;

        var scoredList = candidates
            .Select(c => new
            {
                Product = c,
                Score = CalculateScore(c, targetName, targetStrength, targetForm, targetGenericName)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToList();
        return scoredList.Select(x => x.Product).ToList();
        //var scored = scoredList
        //    .FirstOrDefault();

        //return scored?.Product;
    }

    private static int CalculateScore(
        ScrapedProduct candidate,
        string? targetName,
        string? targetStrength,
        string? targetForm,
        string? targetGenericName,
        int score = 0)
    {
        //var score = 0;

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

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? GetStringOrNumber(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) ? value.ToString() : null;
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

        // Normalize multiple spaces/tabs/newlines to a single space.
        value = Regex.Replace(value, @"\s+", " ");

        // Normalize medicine strength formatting.
        // Examples:
        // 500 mg -> 500mg || 500MG -> 500mg || 500/mg -> 500mg || 500-Mg -> 500mg || 500_mg -> 500mg || 10 / ml-> 10ml
        value = Regex.Replace(value, @"(\d+(?:\.\d+)?)\s*[/\-_]?\s*(mg|mcg|g|kg|ml|l|iu|unit|units|%)\b", "$1$2");

        return value;
    }
}