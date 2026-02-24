using ShopwareDataHydrator.Api;
using ShopwareDataHydrator.Hydrators;
using ShopwareDataHydrator.Models;

var config = ParseConfig(args);
if (config == null)
{
    PrintUsage();
    return 1;
}

Console.WriteLine("=== ShopwareDataHydrator - Shopware 6 Data Hydrator ===");
Console.WriteLine($"URL:            {config.Url}");
Console.WriteLine($"Customers:      {config.CustomerCount}");
Console.WriteLine($"Orders:         {config.OrderCount}");
Console.WriteLine($"Days back:      {config.Days}");
Console.WriteLine($"Failed orders:  {config.FailedOrdersPercent}%");
if (!string.IsNullOrEmpty(config.SalesChannel))
    Console.WriteLine($"Sales channel:  {config.SalesChannel}");
else
    Console.WriteLine($"Sales channel:  (auto-detect)");
if (config.LiveDuration > 0)
{
    Console.WriteLine($"Live mode:      {config.OrderCount} orders over {config.LiveDuration}s (~{(double)config.LiveDuration / config.OrderCount:F1}s interval)");
    Console.WriteLine($"Live status:    {(config.LiveStatus ? "enabled" : "disabled")}");
}
Console.WriteLine();
Console.WriteLine("HINT: Disable mail sending in your Shopware flows to avoid mass emails.");
Console.WriteLine();

var httpClient = new HttpClient
{
    BaseAddress = new Uri(config.Url.TrimEnd('/')),
    Timeout = TimeSpan.FromSeconds(120)
};

var adminApi = new AdminApiClient(httpClient);

Console.Write("Authenticating with Admin API... ");
try
{
    await adminApi.Authenticate(config.User, config.Password);
    Console.WriteLine("OK");
}
catch (Exception ex)
{
    Console.WriteLine($"FAILED: {ex.Message}");
    return 1;
}

Console.Write("Fetching sales channels... ");
var salesChannels = await adminApi.GetSalesChannels();
if (salesChannels.Count == 0)
{
    Console.WriteLine("FAILED: No storefront sales channels found.");
    return 1;
}

SalesChannelInfo salesChannel;
if (!string.IsNullOrEmpty(config.SalesChannel))
{
    var match = salesChannels.FirstOrDefault(sc =>
        sc.Name.Equals(config.SalesChannel, StringComparison.OrdinalIgnoreCase));
    if (match == null)
    {
        Console.WriteLine($"FAILED: Sales channel '{config.SalesChannel}' not found.");
        Console.WriteLine("Available sales channels:");
        foreach (var sc in salesChannels)
            Console.WriteLine($"  - {sc.Name}");
        return 1;
    }
    salesChannel = match;
}
else
{
    salesChannel = salesChannels[0];
}
Console.WriteLine($"OK ({salesChannel.Name})");

Console.Write("Fetching salutations... ");
var salutations = await adminApi.GetSalutations();
Console.WriteLine($"OK ({salutations.Count})");

Console.Write("Fetching countries... ");
var countries = await adminApi.GetCountries();
Console.WriteLine($"OK ({countries.Count})");

Console.Write("Fetching promotion codes... ");
var promotionCodes = await adminApi.GetPromotionCodes();
Console.WriteLine($"OK ({promotionCodes.Count})");

var storefrontApi = new StorefrontApiClient(httpClient, salesChannel.AccessKey, salesChannel.NavigationCategoryId);

Console.Write("Fetching available products... ");
var products = await storefrontApi.GetProducts();
Console.WriteLine($"OK ({products.Count})");

if (products.Count == 0)
{
    Console.WriteLine("ERROR: No available products found in the sales channel.");
    return 1;
}

var shopData = new ShopData
{
    SalesChannel = salesChannel,
    Salutations = salutations,
    Countries = countries,
    PromotionCodes = promotionCodes
};

var customerHydrator = new CustomerHydrator(storefrontApi, adminApi, shopData);
var customers = await customerHydrator.CreateCustomers(config.CustomerCount);

var orderHydrator = new OrderHydrator(storefrontApi, adminApi, shopData, products, customers, config);

if (config.LiveDuration > 0)
    await orderHydrator.CreateOrdersLive(config.OrderCount, config.LiveDuration, config.LiveStatus);
else
    await orderHydrator.CreateOrders(config.OrderCount);

Console.WriteLine();
Console.WriteLine("=== Done! Data hydration complete. ===");
return 0;

static AppConfig? ParseConfig(string[] args)
{
    var config = new AppConfig();

    foreach (var arg in args)
    {
        if (arg is "-h" or "--help" or "-help")
            return null;

        var parts = arg.Split('=', 2);
        if (parts.Length != 2) continue;

        var key = parts[0].TrimStart('-').ToLowerInvariant();
        var value = parts[1];

        switch (key)
        {
            case "url": config.Url = value; break;
            case "user": config.User = value; break;
            case "password": config.Password = value; break;
            case "customers": int.TryParse(value, out var c); config.CustomerCount = c; break;
            case "orders": int.TryParse(value, out var o); config.OrderCount = o; break;
            case "days": int.TryParse(value, out var d); config.Days = d; break;
            case "failed-orders": int.TryParse(value, out var f); config.FailedOrdersPercent = f; break;
            case "sales-channel": config.SalesChannel = value; break;
            case "live": int.TryParse(value, out var l); config.LiveDuration = l; break;
            case "live-status": config.LiveStatus = value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1"; break;
        }
    }

    if (string.IsNullOrEmpty(config.Url) || string.IsNullOrEmpty(config.User) || string.IsNullOrEmpty(config.Password))
        return null;

    if (config.CustomerCount <= 0) config.CustomerCount = 10;
    if (config.OrderCount <= 0) config.OrderCount = 50;
    if (config.Days <= 0) config.Days = 365;
    if (config.FailedOrdersPercent < 0) config.FailedOrdersPercent = 0;
    if (config.FailedOrdersPercent > 100) config.FailedOrdersPercent = 100;

    return config;
}

static void PrintUsage()
{
    Console.WriteLine("ShopwareDataHydrator - Shopware 6 Data Hydrator");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  ShopwareDataHydrator -url=<shop-url> -user=<admin> -password=<pass> [options]");
    Console.WriteLine();
    Console.WriteLine("Required:");
    Console.WriteLine("  -url              Shopware 6 shop URL");
    Console.WriteLine("  -user             Admin username");
    Console.WriteLine("  -password         Admin password");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -customers=N      Number of customers to create (default: 10)");
    Console.WriteLine("  -orders=N         Number of orders to create (default: 50)");
    Console.WriteLine("  -days=N           Backdate range in days from today (default: 365)");
    Console.WriteLine("  -failed-orders=N  Percentage of cancelled/refunded orders (default: 5)");
    Console.WriteLine("  -sales-channel=X  Sales channel name (default: first active storefront)");
    Console.WriteLine("  -live=SECONDS     Live mode: spread orders over given duration");
    Console.WriteLine("  -live-status=1    Apply status transitions in live mode (default: off)");
    Console.WriteLine("  -help             Show this help");
    Console.WriteLine();
    Console.WriteLine("Batch mode (default):");
    Console.WriteLine("  Creates all orders as fast as possible, backdates them across -days range,");
    Console.WriteLine("  and applies status transitions.");
    Console.WriteLine("  - Open orders only appear in the last 5% of the date range.");
    Console.WriteLine("  - Failed orders (cancelled + refunded) are controlled via -failed-orders.");
    Console.WriteLine("  - Remaining orders are split between Completed and InProgress.");
    Console.WriteLine();
    Console.WriteLine("Live mode (-live=SECONDS):");
    Console.WriteLine("  Distributes orders evenly over the given duration, simulating real-time");
    Console.WriteLine("  customer traffic. Useful for testing ERP/WaWi integrations.");
    Console.WriteLine("  Orders keep their current timestamp (no backdating).");
    Console.WriteLine("  Status transitions are off by default (-live-status=1 to enable).");
    Console.WriteLine();
    Console.WriteLine("Note:");
    Console.WriteLine("  Values with spaces must be quoted: -sales-channel=\"Storefront DE\"");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  ShopwareDataHydrator -url=https://myshop.com -user=admin -password=shopware \\");
    Console.WriteLine("    -customers=50 -orders=200 -days=180 -sales-channel=\"Storefront DE\"");
    Console.WriteLine();
    Console.WriteLine("  ShopwareDataHydrator -url=https://myshop.com -user=admin -password=shopware \\");
    Console.WriteLine("    -customers=10 -orders=30 -live=300");
}
