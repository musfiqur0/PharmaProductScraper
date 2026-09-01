using System.Text.Json;
using PharmaProductScraper.Models;

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

    public async Task<ScrapedProduct?> SearchAsync(string productName, CancellationToken ct = default)
    {
        var url =
            $"{SearchUrl}" +
            $"?_type=web" +
            $"&_page=1" +
            $"&_perPage=20" +
            $"&_search={Uri.EscapeDataString(productName)}";

        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("data", out var data))
            return null;

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

        return candidates
            .OrderByDescending(x => GetMatchScore(productName, x.Name ?? string.Empty))
            .FirstOrDefault();
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
        if (string.IsNullOrWhiteSpace(result))
            return 0;

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
        return string.Join(" ", value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}