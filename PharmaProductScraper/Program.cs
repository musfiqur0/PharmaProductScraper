using PharmaProductScraper.Models;
using PharmaProductScraper.Repositories;
using PharmaProductScraper.Scrapers;

var connectionString = "Host=localhost;Port=5432;Database=dg_pharma;Username=postgres;Password=postgrespass123;";
var connectionStringLive= "Host=localhost;Port=5432;Database=dg_pharma;Username=postgres;Password=postgrespass123;";

var delayMilliseconds = 1500;
var take = 100;

var repository = new ProductRepository(connectionString, connectionStringLive);

using var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(30)
};

httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
    "AppleWebKit/537.36 Chrome/124.0 Safari/537.36");

httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/json");

var medexScraper = new MedexScraper(httpClient);
var aroggaScraper = new AroggaScraper(httpClient);

var products = await repository.GetProductsAsync(take);

Console.WriteLine($"Products to process: {products.Count}");

var success = 0;
var notFound = 0;
var failed = 0;

foreach (var product in products)
{
    Console.WriteLine();
    Console.WriteLine($"[{product.Id}] {product.Name}");

    try
    {
        ScrapedProduct? result = await aroggaScraper.SearchAsync(product);
        //ScrapedProduct? result = null;

        if (result is null)
        {
            Console.WriteLine("Arogga: Not found. Trying MedEx...");
            result = await medexScraper.SearchAsync(product);
        }

        if (result is null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No match found.");
            Console.ResetColor();

            notFound++;

            await Task.Delay(delayMilliseconds);

            continue;
        }

        Console.WriteLine($"Source   : {result.Source}");
        Console.WriteLine($"Found    : {result.Name}");
        Console.WriteLine($"Generic  : {result.GenericName}");
        Console.WriteLine($"Strength : {result.Strength}");
        Console.WriteLine($"URL      : {result.ProductUrl}");

        await repository.UpdateProductAsync(
            product.Id,
            result);

        success++;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Updated successfully.");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        failed++;

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"ERROR: {ex.Message}");
        Console.ResetColor();
    }

    await Task.Delay(delayMilliseconds);
}

Console.WriteLine();
Console.WriteLine("===================================");
Console.WriteLine($"Success   : {success}");
Console.WriteLine($"Not found : {notFound}");
Console.WriteLine($"Failed    : {failed}");
Console.WriteLine("===================================");