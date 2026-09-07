using System.Net;
using System.Text.Json;

public static class ProxyHelper
{
    private const string ProxyApiUrl = "https://proxylist.geonode.com/api/proxy-list?page=1&limit=500&sort_by=responseTime&sort_type=asc";
    private const string IpCheckUrl = "https://api.ipify.org";
    private const string MedexTestUrl = "https://medex.com.bd/brands/8860/cleocin-300-mg-capsule";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0 Safari/537.36";

    public static async Task<HttpClient?> CreateWorkingHttpClientAsync()
    {
        var httpsProxies = await LoadHttpsProxiesAsync();

        if (httpsProxies.Count == 0)
        {
            WriteError("No HTTPS proxies found.");
            return null;
        }

        Console.WriteLine($"HTTPS proxies found: {httpsProxies.Count}");
        Console.WriteLine();

        var selectedProxyAddress = await FindWorkingProxyAsync(httpsProxies);

        if (string.IsNullOrWhiteSpace(selectedProxyAddress))
        {
            WriteError("No HTTPS proxy working with MedEx was found.");
            return null;
        }

        Console.ForegroundColor = ConsoleColor.Green;

        Console.WriteLine();
        Console.WriteLine($"Selected proxy: {selectedProxyAddress}");
        Console.ResetColor();

        return CreateHttpClient(selectedProxyAddress, TimeSpan.FromSeconds(30));
    }

    private static async Task<List<(string Ip, int Port)>> LoadHttpsProxiesAsync()
    {
        Console.WriteLine("Loading proxy list from GeoNode...");

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        string json;

        try
        {
            json = await client.GetStringAsync(ProxyApiUrl);
        }
        catch (Exception ex)
        {
            WriteError($"Failed to load proxy API: {ex.Message}");
            return new List<(string Ip, int Port)>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("data", out var proxyData) || proxyData.ValueKind != JsonValueKind.Array)
            {
                WriteError("GeoNode returned no proxy data.");
                return new List<(string Ip, int Port)>();
            }

            var proxies = new List<(string Ip, int Port)>();

            foreach (var item in proxyData.EnumerateArray())
            {
                if (!item.TryGetProperty("ip", out var ipElement))
                    continue;

                if (!item.TryGetProperty("port", out var portElement))
                    continue;

                if (!item.TryGetProperty("protocols", out var protocolsElement))
                    continue;

                if (protocolsElement.ValueKind != JsonValueKind.Array)
                    continue;

                var supportsHttps = protocolsElement
                        .EnumerateArray()
                        .Any(x => string.Equals(x.GetString(), "https", StringComparison.OrdinalIgnoreCase));

                if (!supportsHttps)
                    continue;

                var ip = ipElement.GetString();

                if (string.IsNullOrWhiteSpace(ip))
                    continue;

                if (!int.TryParse(portElement.ToString(), out var port))
                    continue;

                proxies.Add((ip, port));
            }

            return proxies;
        }
        catch (Exception ex)
        {
            WriteError($"Failed to parse proxy API: {ex.Message}");
            return new List<(string Ip, int Port)>();
        }
    }

    private static async Task<string?> FindWorkingProxyAsync(List<(string Ip, int Port)> proxies)
    {
        var index = 0;
        foreach (var proxyItem in proxies)
        {
            index++;

            // GeoNode HTTPS proxy normally uses
            // HTTP CONNECT to reach HTTPS destinations.
            var proxyAddress = $"http://{proxyItem.Ip}:{proxyItem.Port}";

            Console.WriteLine($"[{index}/{proxies.Count}] " + $"Testing: {proxyAddress}");

            try
            {
                using var client = CreateHttpClient(proxyAddress, TimeSpan.FromSeconds(5));

                var proxyIp = await client.GetStringAsync(IpCheckUrl);

                proxyIp = proxyIp.Trim();

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"    IP WORKS | Public IP: {proxyIp}");
                Console.ResetColor();

                using var medexResponse = await client.GetAsync(MedexTestUrl);

                var medexHtml = await medexResponse.Content.ReadAsStringAsync();

                if (!medexResponse.IsSuccessStatusCode)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"    MEDEX FAILED | HTTP " + $"{(int)medexResponse.StatusCode} " + $"{medexResponse.StatusCode}");
                    Console.ResetColor();

                    continue;
                }

                if (medexHtml.Contains("Security Check", StringComparison.OrdinalIgnoreCase))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("    MEDEX BLOCKED | Security Check");
                    Console.ResetColor();

                    continue;
                }

                if (!medexHtml.Contains("Cleocin", StringComparison.OrdinalIgnoreCase))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("    MEDEX FAILED | Invalid product page");
                    Console.ResetColor();

                    continue;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("    SUCCESS | HTTPS + MedEx working");
                Console.WriteLine($"    Proxy IP: {proxyIp}");
                Console.ResetColor();

                return proxyAddress;
            }
            catch (TaskCanceledException)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("    FAILED | Timeout");
                Console.ResetColor();
            }
            catch (HttpRequestException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;

                if (ex.Message.Contains("407", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine("    FAILED | 407 Authentication Required");
                else
                    Console.WriteLine($"    FAILED | {ex.Message}");

                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"    FAILED | {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine();
        }

        return null;
    }

    private static HttpClient CreateHttpClient(string proxyAddress, TimeSpan timeout)
    {
        var proxy = new WebProxy { Address = new Uri(proxyAddress), BypassProxyOnLocal = false, UseDefaultCredentials = false };
        var handler = new HttpClientHandler { Proxy = proxy, UseProxy = true };
        var client = new HttpClient(handler) { Timeout = timeout };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/json");

        return client;
    }

    private static void WriteError(
        string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}