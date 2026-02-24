namespace ShopwareDataHydrator.Api;

using System.Text;
using System.Text.Json.Nodes;
using ShopwareDataHydrator.Models;

public class StorefrontApiClient
{
    private readonly HttpClient _http;
    private readonly string _accessKey;
    private readonly string _navigationCategoryId;

    public StorefrontApiClient(HttpClient http, string accessKey, string navigationCategoryId)
    {
        _http = http;
        _accessKey = accessKey;
        _navigationCategoryId = navigationCategoryId;
    }

    private async Task<(JsonNode? Body, string ContextToken)> Send(
        HttpMethod method, string url, object? body = null, string? contextToken = null)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var req = new HttpRequestMessage(method, url);
            req.Headers.Add("sw-access-key", _accessKey);
            req.Headers.Add("Accept", "application/json");

            if (!string.IsNullOrEmpty(contextToken))
                req.Headers.Add("sw-context-token", contextToken);

            if (body is JsonNode jsonNode)
                req.Content = new StringContent(jsonNode.ToJsonString(), Encoding.UTF8, "application/json");
            else if (method == HttpMethod.Post || method == HttpMethod.Patch)
                req.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var resp = await _http.SendAsync(req);

            if (resp.StatusCode == (System.Net.HttpStatusCode)429)
            {
                var retryBody = await resp.Content.ReadAsStringAsync();
                var seconds = 3;
                try
                {
                    var parsed = JsonNode.Parse(retryBody);
                    seconds = parsed?["errors"]?.AsArray().FirstOrDefault()?["meta"]?["parameters"]?["seconds"]?.GetValue<int>() ?? 3;
                }
                catch { }
                Console.WriteLine($"    Rate limited, waiting {seconds}s...");
                await Task.Delay(TimeSpan.FromSeconds(seconds + 1));
                continue;
            }

            if (!resp.IsSuccessStatusCode)
            {
                var errorBody = await resp.Content.ReadAsStringAsync();
                throw new HttpRequestException(
                    $"Storefront API {method} {url} returned {(int)resp.StatusCode}: {errorBody}");
            }

            var newToken = resp.Headers.TryGetValues("sw-context-token", out var tokens)
                ? tokens.FirstOrDefault() ?? contextToken ?? ""
                : contextToken ?? "";

            var content = await resp.Content.ReadAsStringAsync();
            var json = string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);

            return (json, newToken);
        }

        throw new HttpRequestException($"Storefront API {method} {url} failed after 5 retries (rate limited)");
    }

    public async Task<List<ProductInfo>> GetProducts()
    {
        var products = new List<ProductInfo>();
        var page = 1;
        var url = $"/store-api/product-listing/{_navigationCategoryId}";

        while (true)
        {
            var body = new JsonObject
            {
                ["limit"] = 100,
                ["p"] = page
            };

            var (json, _) = await Send(HttpMethod.Post, url, body);
            var elements = json?["elements"]?.AsArray();
            if (elements == null || elements.Count == 0) break;

            foreach (var item in elements)
            {
                var available = item!["available"]?.GetValue<bool>() ?? false;
                if (!available) continue;

                var name = item["translated"]?["name"]?.GetValue<string>()
                           ?? item["name"]?.GetValue<string>() ?? "";

                products.Add(new ProductInfo
                {
                    Id = item["id"]!.GetValue<string>(),
                    Name = name
                });
            }

            var total = json?["total"]?.GetValue<int>() ?? 0;
            if (page * 100 >= total) break;
            page++;
        }

        return products;
    }

    public async Task<(string CustomerId, string ContextToken)> RegisterCustomer(
        string salutationId, string firstName, string lastName, string email,
        string password, string street, string zipcode, string city,
        string countryId, string storefrontUrl, bool guest = false)
    {
        var body = new JsonObject
        {
            ["salutationId"] = salutationId,
            ["firstName"] = firstName,
            ["lastName"] = lastName,
            ["email"] = email,
            ["storefrontUrl"] = storefrontUrl,
            ["billingAddress"] = new JsonObject
            {
                ["salutationId"] = salutationId,
                ["firstName"] = firstName,
                ["lastName"] = lastName,
                ["street"] = street,
                ["zipcode"] = zipcode,
                ["city"] = city,
                ["countryId"] = countryId
            },
            ["acceptedDataProtection"] = true
        };

        if (guest)
            body["guest"] = true;
        else
            body["password"] = password;

        var (json, token) = await Send(HttpMethod.Post, "/store-api/account/register", body);
        var customerId = json?["id"]?.GetValue<string>() ?? "";

        return (customerId, token);
    }

    public async Task<string> Login(string email, string password)
    {
        var body = new JsonObject
        {
            ["email"] = email,
            ["password"] = password
        };
        var (json, token) = await Send(HttpMethod.Post, "/store-api/account/login", body);

        if (!string.IsNullOrEmpty(token))
            return token;

        return json?["contextToken"]?.GetValue<string>() ?? "";
    }

    public async Task<string> AddProducts(string contextToken, List<(string ProductId, int Quantity)> items)
    {
        var jsonItems = new JsonArray();
        foreach (var (productId, quantity) in items)
        {
            jsonItems.Add((JsonNode)new JsonObject
            {
                ["type"] = "product",
                ["referencedId"] = productId,
                ["quantity"] = quantity
            });
        }

        var body = new JsonObject { ["items"] = jsonItems };
        var (_, newToken) = await Send(HttpMethod.Post, "/store-api/checkout/cart/line-item", body, contextToken);
        return newToken;
    }

    public async Task<string> TryAddPromotionCode(string contextToken, string code)
    {
        try
        {
            var body = new JsonObject
            {
                ["items"] = new JsonArray
                {
                    (JsonNode)new JsonObject
                    {
                        ["type"] = "promotion",
                        ["referencedId"] = code
                    }
                }
            };

            var (_, newToken) = await Send(HttpMethod.Post, "/store-api/checkout/cart/line-item", body, contextToken);
            return newToken;
        }
        catch
        {
            return contextToken;
        }
    }

    public async Task<string> SwitchPaymentMethod(string contextToken, string paymentMethodId)
    {
        var body = new JsonObject { ["paymentMethodId"] = paymentMethodId };
        var (_, newToken) = await Send(HttpMethod.Patch, "/store-api/context", body, contextToken);
        return newToken;
    }

    public async Task<int> GetCartItemCount(string contextToken)
    {
        var (json, _) = await Send(HttpMethod.Get, "/store-api/checkout/cart", contextToken: contextToken);
        var lineItems = json?["lineItems"]?.AsArray();
        return lineItems?.Count ?? 0;
    }

    public async Task<string?> PlaceOrder(string contextToken)
    {
        var itemCount = await GetCartItemCount(contextToken);
        if (itemCount == 0)
            throw new InvalidOperationException("Cart is empty, skipping order");

        var (json, _) = await Send(HttpMethod.Post, "/store-api/checkout/order", null, contextToken);
        return json?["id"]?.GetValue<string>();
    }
}
