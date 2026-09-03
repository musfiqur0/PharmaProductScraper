namespace PharmaProductScraper.Models;

public sealed class ScrapedProduct
{
    public string Source { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string? Name { get; set; }
    public string? GenericName { get; set; }

    public string? Type { get; set; }  // Database "type"
    public string? Category { get; set; }  // For MedEx this will be the dosage form, e.g. Tablet.
    public string? Strength { get; set; }
    public string? ProductUrl { get; set; }
    public bool? IsPrescriptionRequired { get; set; } = null;
    public int? MedicinePerStrips { get; set; }
    public string? Manufacturer { get; set; }
    public double? Price { get; set; }
    public int? PackSize { get; set; }
    public string? Size { get; set; }
    public Dictionary<string, string> Monograph { get; set; } = new();
    public string? ImageUrl { get; set; }
}

