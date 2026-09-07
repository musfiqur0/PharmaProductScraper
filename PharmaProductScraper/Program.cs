using PharmaProductScraper.Models;
using PharmaProductScraper.Repositories;
using PharmaProductScraper.Scrapers;


static HttpClient CreateDefaultHttpClient()
{
    var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0 Safari/537.36");
    client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/json");
    return client;
}


var connectionString = "Host=localhost;Port=5432;Database=dg_pharma;Username=postgres;Password=postgrespass123;";
var connectionStringLive = "Host=localhost;Port=5432;Database=dg_pharma;Username=postgres;Password=postgrespass123;";

var take = 1000;

var repository = new ProductRepository(connectionString, connectionStringLive);

HttpClient? proxyHttpClient = null;
proxyHttpClient = await ProxyHelper.CreateWorkingHttpClientAsync();
using var httpClient = proxyHttpClient ?? CreateDefaultHttpClient();

Console.WriteLine(proxyHttpClient is null ? "No working proxy found. Using direct connection." : "Using proxy connection.");

// ======================================================
// SCRAPERS
// ======================================================

var medexScraper = new MedexScraper(httpClient);
var aroggaScraper = new AroggaScraper(httpClient);

var products = await repository.GetProductsAsync(take);

Console.WriteLine($"Products to process: {products.Count}");

var success = 0;
var notFound = 0;
var failed = 0;
var scraperAttempt = 0;

// ======================================================
// EXISTING SCRAPER FLOW
// ======================================================

foreach (var product in products)
{
    scraperAttempt++;
    Console.WriteLine();
    Console.WriteLine("===================================");
    Console.WriteLine($"Iteration {scraperAttempt}/{products.Count} | Product ID: {product.Id} | {product.Name}");

    try
    {
        ScrapedProduct? result;

        //if (scraperAttempt % 4 == 0)
        //{
        //    Console.WriteLine("Trying MedEx first...");
        //    result = await medexScraper.SearchAsync(product);

        //    if (result is null)
        //    {
        //        Console.WriteLine("MedEx: Not found. Trying Arogga...");
        //        result = await aroggaScraper.SearchAsync(product);
        //    }
        //}
        //else
        //{
        //    Console.WriteLine("Trying Arogga first...");
        //    result = await aroggaScraper.SearchAsync(product);

        //    if (result is null)
        //    {
        //        Console.WriteLine("Arogga: Not found. Trying MedEx...");
        //        result = await medexScraper.SearchAsync(product);
        //    }
        //}

        Console.WriteLine("Trying Arogga first...");
        result = await aroggaScraper.SearchAsync(product);

        if (result is null)
        {
            Console.WriteLine("Arogga: Not found. Trying MedEx...");
            result = await medexScraper.SearchAsync(product);
        }

        if (result is null)
        {
            await repository.InsertNotFoundProductAsync(product.Id);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No match found.");
            Console.ResetColor();

            notFound++;

            var delayMilliseconds = Random.Shared.Next(2000, 10001);
            Console.WriteLine($"Waiting {delayMilliseconds / 1000.0:F1} seconds...");
            await Task.Delay(delayMilliseconds);

            continue;
        }

        Console.WriteLine($"Source   : {result.Source}");
        Console.WriteLine($"Found    : {result.Name}");
        Console.WriteLine($"Generic  : {result.GenericName}");
        Console.WriteLine($"Strength : {result.Strength}");
        Console.WriteLine($"URL      : {result.ProductUrl}");

        await repository.UpdateProductAsync(product.Id, result);

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

    var delayMilliseconds2 = Random.Shared.Next(2000, 10001);
    Console.WriteLine($"Waiting {delayMilliseconds2 / 1000.0:F1} seconds...");
    await Task.Delay(delayMilliseconds2);
}

// ======================================================
// SUMMARY
// ======================================================

Console.WriteLine();
Console.WriteLine("===================================");
Console.WriteLine($"Success   : {success}");
Console.WriteLine($"Not found : {notFound}");
Console.WriteLine($"Failed    : {failed}");
Console.WriteLine("===================================");