namespace PharmaProductScraper.Models;

public sealed class ScrapedProduct
{
    public string Source { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string? Name { get; set; }
    public string? GenericName { get; set; }
    public string? Manufacturer { get; set; }
    public string? Type { get; set; }
    public string? Strength { get; set; }
    public string? ProductUrl { get; set; }
    public string? ImageUrl { get; set; }
    public double? Price { get; set; }
    public int? PackSize { get; set; }
    public Dictionary<string, string> Monograph { get; set; } = new();
}

