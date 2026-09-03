namespace PharmaProductScraper.Models;

public sealed class Product
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? Strength { get; set; }
    public string? Form { get; set; }
    public string? Type { get; set; }
}