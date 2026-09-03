using PharmaProductScraper.Models;
using System.Globalization;
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

            if (match is null)
                return null;
        }

        // 2. Then filter with strength
        if (!string.IsNullOrWhiteSpace(strength))
        {
            match = FindBestMatch(match, null, strength, null, null);

            if (match is null)
                return null;
        }

        // 3. Then filter with p_form
        if (!string.IsNullOrWhiteSpace(form))
        {
            match = FindBestMatch(match, null, null, form, null);

            if (match is null)
                return null;
        }

        // 4. Then filter with p_generic_name
        if (!string.IsNullOrWhiteSpace(genericName))
        {
            match = FindBestMatch(match, null, null, null, genericName);

            if (match is null)
                return null;
        }

        var result = match.FirstOrDefault();

        if (result is not null)
        {
            // Search/matching is already complete.
            // Now fetch only the selected product detail page
            // and populate the additional required fields.
            await EnrichFromProductPageAsync(result, ct);
        }

        return result;
    }

    private async Task<List<ScrapedProduct>> FetchCandidatesAsync(
        string query,
        CancellationToken ct)
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
                    Category = form, // category  // Keep same behavior as existing medicine form.
                    // url
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

    private async Task EnrichFromProductPageAsync(ScrapedProduct result, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(result.ProductUrl) || string.IsNullOrWhiteSpace(result.ExternalId))
            return;

        try
        {
            using var response = await _httpClient.GetAsync(result.ProductUrl, ct);

            if (!response.IsSuccessStatusCode)
                return;

            var html = await response.Content.ReadAsStringAsync(ct);

            // Arogga redirects /product/{id}
            // to the full slug URL.
            var finalUrl = response.RequestMessage?.RequestUri?.ToString();
            if (!string.IsNullOrWhiteSpace(finalUrl))
                result.ProductUrl = finalUrl;

            var productBlock = GetProductBlock(html, result.ExternalId);
            if (string.IsNullOrWhiteSpace(productBlock))
                return;

            var name = GetEmbeddedString(productBlock, "p_name");
            if (!string.IsNullOrWhiteSpace(name))
                result.Name = name;

            var genericName = GetEmbeddedString(productBlock, "p_generic_name");
            if (!string.IsNullOrWhiteSpace(genericName))
                result.GenericName = genericName.Trim();

            // type + category
            // p_form = Tablet / Capsule / Syrup / etc.
            var form = GetEmbeddedString(productBlock, "p_form");
            if (!string.IsNullOrWhiteSpace(form))
            {
                result.Type = form;
                result.Category = form;
            }

            var strength = GetEmbeddedString(productBlock, "p_strength");

            if (!string.IsNullOrWhiteSpace(strength))
                result.Strength = strength;

            // -------------------------------------------------
            // is_prescription_required
            //
            // Arogga:
            // p_rx_req = 1 -> prescription required
            // p_rx_req = 0 -> prescription not required
            // -------------------------------------------------
            var rxRequired = GetEmbeddedInt(productBlock, "p_rx_req");

            if (rxRequired.HasValue)
                result.IsPrescriptionRequired = rxRequired.Value == 1;

            // -------------------------------------------------
            // medicine_per_strips
            //
            // Example:
            // pu_b2c_base_unit_multiplier = 10
            //
            // Strip = 10 Tablets
            // -------------------------------------------------
            var unitsPerStrip = GetEmbeddedInt(productBlock, "pu_b2c_base_unit_multiplier");

            if (unitsPerStrip.HasValue)
                result.MedicinePerStrips = unitsPerStrip.Value;

            // -------------------------------------------------
            // rate_per_unit
            //
            // You specified:
            // displayed MRP ৳12 should be used.
            //
            // Arogga:
            // pv_b2c_mrp = 12
            // -------------------------------------------------
            var mrp = GetEmbeddedDouble(productBlock, "pv_b2c_mrp");
            if (mrp.HasValue)
                result.Price = mrp.Value;

            // -------------------------------------------------
            // pack_size
            //
            // Same units-per-strip value.
            // Example: 10 tablets per strip -> 10
            // -------------------------------------------------
            if (unitsPerStrip.HasValue)
                result.PackSize = unitsPerStrip.Value;

            // -------------------------------------------------
            // size
            //
            // Example:
            // pu_base_unit_label = Tablet
            // pu_b2c_sales_unit_label = Strip
            // pu_b2c_base_unit_multiplier = 10
            //
            // Result:
            // 10 Tablets (1 Strip)
            // -------------------------------------------------
            var baseUnitLabel = GetEmbeddedString(productBlock, "pu_base_unit_label");
            var salesUnitLabel = GetEmbeddedString(productBlock, "pu_b2c_sales_unit_label");

            var size = BuildSize(unitsPerStrip, baseUnitLabel, salesUnitLabel);
            if (!string.IsNullOrWhiteSpace(size))
                result.Size = size;

            // -------------------------------------------------
            // monograph
            //
            // Arogga:
            // p_description.generic.brief_description
            // -------------------------------------------------
            var monograph = ExtractMonograph(productBlock);
            if (monograph.Count > 0)
                result.Monograph = monograph;
        }
        catch
        {
            // Detail enrichment should never break
            // the existing working Arogga search result.
        }
    }

    private static string? GetProductBlock(
        string html,
        string productId)
    {
        var escapedMarker = $"\\\"p_id\\\":{productId}";

        var plainMarker = $"\"p_id\":{productId}";

        var start = html.IndexOf(escapedMarker, StringComparison.Ordinal);

        if (start < 0)
            start = html.IndexOf(plainMarker, StringComparison.Ordinal);

        if (start < 0)
            return null;

        // All required product properties are close to
        // the main product object.
        var length = Math.Min(40000, html.Length - start);

        return html.Substring(start, length);
    }

    private static string? GetEmbeddedString(string source, string property)
    {
        // Next.js escaped JSON:
        // \"p_form\":\"Tablet\"
        var escapedMarker = $"\\\"{property}\\\":\\\"";

        var start = source.IndexOf(escapedMarker, StringComparison.Ordinal);

        if (start >= 0)
        {
            start += escapedMarker.Length;

            var end = source.IndexOf("\\\"", start, StringComparison.Ordinal);

            if (end > start)
                return source.Substring(start, end - start).Trim();
        }

        // Normal JSON fallback:
        // "p_form":"Tablet"
        var normalMarker = $"\"{property}\":\"";

        start = source.IndexOf(normalMarker, StringComparison.Ordinal);

        if (start < 0)
            return null;

        start += normalMarker.Length;

        var normalEnd = source.IndexOf('"', start);

        if (normalEnd <= start)
            return null;

        return source.Substring(start, normalEnd - start).Trim();
    }

    private static int? GetEmbeddedInt(string source, string property)
    {
        var value = GetEmbeddedNumber(source, property);

        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            return number;

        return null;
    }

    private static double? GetEmbeddedDouble(string source, string property)
    {
        var value = GetEmbeddedNumber(source, property);

        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            return number;

        return null;
    }

    private static string? GetEmbeddedNumber(string source, string property)
    {
        var escapedMarker = $"\\\"{property}\\\":";

        var start = source.IndexOf(escapedMarker, StringComparison.Ordinal);

        if (start >= 0)
        {
            start += escapedMarker.Length;

            return ReadNumber(source, start);
        }

        var normalMarker = $"\"{property}\":";

        start = source.IndexOf(normalMarker, StringComparison.Ordinal);

        if (start < 0)
            return null;

        start += normalMarker.Length;

        return ReadNumber(source, start);
    }

    private static string? ReadNumber(string source, int start)
    {
        while (start < source.Length && char.IsWhiteSpace(source[start]))
        {
            start++;
        }

        if (start >= source.Length)
            return null;

        var match = Regex.Match(source.Substring(start), @"^-?\d+(?:\.\d+)?");

        return match.Success
            ? match.Value
            : null;
    }

    private static string? BuildSize(int? unitsPerStrip, string? baseUnitLabel, string? salesUnitLabel)
    {
        if (!unitsPerStrip.HasValue || unitsPerStrip.Value <= 0 || string.IsNullOrWhiteSpace(baseUnitLabel))
            return null;

        var unit = baseUnitLabel.Trim();

        if (unitsPerStrip.Value > 1 && !unit.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            unit += "s";

        if (string.IsNullOrWhiteSpace(salesUnitLabel))
            return $"{unitsPerStrip.Value} {unit}";

        var salesUnit = salesUnitLabel.Trim();

        if (string.Equals(baseUnitLabel, salesUnitLabel, StringComparison.OrdinalIgnoreCase))
            return $"{unitsPerStrip.Value} {unit}";

        return $"{unitsPerStrip.Value} {unit} (1 {salesUnit})";
    }

    private static Dictionary<string, string> ExtractMonograph(string productBlock)
    {
        var result = new Dictionary<string, string>();

        try
        {
            var escapedStartMarker = "\\\"p_description\\\":";
            var escapedEndMarker = ",\\\"p_generic_name\\\"";
            var start = productBlock.IndexOf(escapedStartMarker, StringComparison.Ordinal);

            string json;

            if (start >= 0)
            {
                start += escapedStartMarker.Length;

                var end = productBlock.IndexOf(escapedEndMarker, start, StringComparison.Ordinal);

                if (end <= start)
                    return result;

                json = productBlock.Substring(start, end - start);

                // Convert Next.js escaped JSON
                // into normal JSON.
                json = json.Replace("\\\"", "\"");
            }
            else
            {
                var normalStartMarker = "\"p_description\":";

                var normalEndMarker = ",\"p_generic_name\"";

                start = productBlock.IndexOf(normalStartMarker, StringComparison.Ordinal);

                if (start < 0)
                    return result;

                start += normalStartMarker.Length;

                var end = productBlock.IndexOf(normalEndMarker, start, StringComparison.Ordinal);

                if (end <= start)
                    return result;

                json = productBlock.Substring(start, end - start);
            }

            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("generic", out var generic))
                return result;

            if (!generic.TryGetProperty("brief_description", out var descriptions) || descriptions.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in descriptions.EnumerateArray())
            {
                if (!item.TryGetProperty("title", out var titleElement) || !item.TryGetProperty("content", out var contentElement))
                {
                    continue;
                }

                var title = titleElement.GetString();

                var content = contentElement.GetString();

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
                    continue;

                // Next.js reference placeholder,
                // e.g. "$4f" is not actual monograph text.
                if (content.StartsWith("$", StringComparison.Ordinal))
                    continue;

                var key = GetMonographKey(title);

                var value = NormalizeMonographText(content);

                if (!string.IsNullOrWhiteSpace(value))
                    result[key] = value;
            }
        }
        catch
        {
            // Keep monograph empty if page structure changes.
        }

        return result;
    }

    private static string GetMonographKey(
        string title)
    {
        return title.Trim().ToLowerInvariant() switch
        {
            "indication" => "indication",
            "administration" => "administration",
            "adult dose" => "adult_dose",
            "child dose" => "child_dose",
            "contraindication" => "contraindication",
            "mode of action" => "pharmacology",
            "precaution" => "precaution",
            "side effect" => "side_effect",
            "interaction" => "interaction",
            _ => Regex.Replace(title.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_')
        };
    }

    private static string NormalizeMonographText(string value)
    {
        value = value.Replace("\\n", " ");
        value = value.Replace("\\r", " ");
        value = Regex.Replace(value, @"\s+", " ");
        return value.Trim();
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

        // Normalize multiple spaces/tabs/newlines.
        value = Regex.Replace(value, @"\s+", " ");

        // Normalize medicine strength formatting.
        //
        // 500 mg -> 500mg
        // 500MG  -> 500mg
        // 500/mg -> 500mg
        // 500-Mg -> 500mg
        // 500_mg -> 500mg
        // 10 / ml -> 10ml
        value = Regex.Replace(value, @"(\d+(?:\.\d+)?)\s*[/\-_]?\s*(mg|mcg|g|kg|ml|l|iu|unit|units|%)\b", "$1$2");

        return value;
    }
}