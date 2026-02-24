namespace ShopwareDataHydrator.Api;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShopwareDataHydrator.Models;

public class AdminApiClient
{
    private readonly HttpClient _http;
    private string _token = "";
    private DateTime _tokenExpiry = DateTime.MinValue;
    private string _user = "";
    private string _pass = "";

    public AdminApiClient(HttpClient http) => _http = http;

    public async Task Authenticate(string user, string pass)
    {
        _user = user;
        _pass = pass;
        await RefreshToken();
    }

    private async Task RefreshToken()
    {
        var body = new JsonObject
        {
            ["grant_type"] = "password",
            ["client_id"] = "administration",
            ["username"] = _user,
            ["password"] = _pass
        };

        var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("/api/oauth/token", content);
        resp.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
        _token = json!["access_token"]!.GetValue<string>();
        _tokenExpiry = DateTime.UtcNow.AddSeconds(json["expires_in"]!.GetValue<int>() - 60);
    }

    private async Task<JsonNode?> Send(HttpMethod method, string url, object? body = null)
    {
        if (DateTime.UtcNow >= _tokenExpiry)
            await RefreshToken();

        var req = BuildRequest(method, url, body);
        var resp = await _http.SendAsync(req);

        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await RefreshToken();
            req = BuildRequest(method, url, body);
            resp = await _http.SendAsync(req);
        }

        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Admin API {method} {url} returned {(int)resp.StatusCode}: {errorBody}");
        }

        var content = await resp.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, object? body)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("Authorization", $"Bearer {_token}");
        req.Headers.Add("Accept", "application/json");

        if (body is JsonNode jsonNode)
            req.Content = new StringContent(jsonNode.ToJsonString(), Encoding.UTF8, "application/json");
        else if (method == HttpMethod.Post || method == HttpMethod.Patch)
            req.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        return req;
    }

    public async Task<List<SalesChannelInfo>> GetSalesChannels()
    {
        var body = new JsonObject
        {
            ["limit"] = 25,
            ["filter"] = new JsonArray
            {
                (JsonNode)new JsonObject
                {
                    ["type"] = "equals",
                    ["field"] = "typeId",
                    ["value"] = "8a243080f92e4c719546314b577cf82b"
                }
            },
            ["associations"] = new JsonObject
            {
                ["domains"] = new JsonObject()
            }
        };

        var json = await Send(HttpMethod.Post, "/api/search/sales-channel", body);
        var list = new List<SalesChannelInfo>();

        foreach (var item in json!["data"]!.AsArray())
        {
            var domains = item!["domains"]?.AsArray();
            var domainUrl = domains?.FirstOrDefault()?["url"]?.GetValue<string>() ?? "";

            list.Add(new SalesChannelInfo
            {
                Id = item["id"]!.GetValue<string>(),
                AccessKey = item["accessKey"]!.GetValue<string>(),
                Name = item["translated"]?["name"]?.GetValue<string>()
                       ?? item["name"]?.GetValue<string>() ?? "Storefront",
                DomainUrl = domainUrl,
                NavigationCategoryId = item["navigationCategoryId"]?.GetValue<string>() ?? "",
                CountryId = item["countryId"]?.GetValue<string>() ?? "",
                PaymentMethodId = item["paymentMethodId"]?.GetValue<string>() ?? "",
                ShippingMethodId = item["shippingMethodId"]?.GetValue<string>() ?? ""
            });
        }

        return list;
    }

    public async Task<List<SalutationInfo>> GetSalutations()
    {
        var json = await Send(HttpMethod.Get, "/api/salutation?limit=25");
        var list = new List<SalutationInfo>();

        foreach (var item in json!["data"]!.AsArray())
        {
            list.Add(new SalutationInfo
            {
                Id = item!["id"]!.GetValue<string>(),
                SalutationKey = item["salutationKey"]!.GetValue<string>()
            });
        }

        return list;
    }

    public async Task<List<CountryInfo>> GetCountries()
    {
        var body = new JsonObject
        {
            ["limit"] = 100,
            ["filter"] = new JsonArray
            {
                (JsonNode)new JsonObject
                {
                    ["type"] = "equals",
                    ["field"] = "active",
                    ["value"] = true
                }
            }
        };

        var json = await Send(HttpMethod.Post, "/api/search/country", body);
        var list = new List<CountryInfo>();

        foreach (var item in json!["data"]!.AsArray())
        {
            list.Add(new CountryInfo
            {
                Id = item!["id"]!.GetValue<string>(),
                Iso = item["iso"]?.GetValue<string>() ?? "",
                Name = item["translated"]?["name"]?.GetValue<string>()
                       ?? item["name"]?.GetValue<string>() ?? ""
            });
        }

        return list;
    }

    public async Task<List<string>> GetPromotionCodes()
    {
        var codes = new List<string>();

        try
        {
            var body = new JsonObject
            {
                ["limit"] = 100,
                ["filter"] = new JsonArray
                {
                    (JsonNode)new JsonObject
                    {
                        ["type"] = "equals",
                        ["field"] = "active",
                        ["value"] = true
                    },
                    (JsonNode)new JsonObject
                    {
                        ["type"] = "equals",
                        ["field"] = "useCodes",
                        ["value"] = true
                    }
                },
                ["associations"] = new JsonObject
                {
                    ["individualCodes"] = new JsonObject { ["limit"] = 100 }
                }
            };

            var json = await Send(HttpMethod.Post, "/api/search/promotion", body);

            foreach (var item in json!["data"]!.AsArray())
            {
                var code = item!["code"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(code))
                    codes.Add(code);

                var individualCodes = item["individualCodes"]?.AsArray();
                if (individualCodes != null)
                {
                    foreach (var ic in individualCodes)
                    {
                        var icCode = ic!["code"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(icCode))
                            codes.Add(icCode);
                    }
                }
            }
        }
        catch
        {
        }

        return codes;
    }

    public async Task<OrderDetails> GetOrderDetails(string orderId)
    {
        var body = new JsonObject
        {
            ["limit"] = 1,
            ["ids"] = new JsonArray { (JsonNode)orderId },
            ["associations"] = new JsonObject
            {
                ["transactions"] = new JsonObject(),
                ["deliveries"] = new JsonObject()
            }
        };

        var json = await Send(HttpMethod.Post, "/api/search/order", body);
        var order = json!["data"]!.AsArray().First();

        return new OrderDetails
        {
            OrderId = orderId,
            TransactionId = order!["transactions"]?.AsArray().FirstOrDefault()?["id"]?.GetValue<string>() ?? "",
            DeliveryId = order["deliveries"]?.AsArray().FirstOrDefault()?["id"]?.GetValue<string>() ?? ""
        };
    }

    public async Task UpdateOrderDate(string orderId, DateTimeOffset date)
    {
        var body = new JsonObject
        {
            ["orderDateTime"] = date.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.000+00:00")
        };

        await Send(HttpMethod.Patch, $"/api/order/{orderId}", body);
    }

    public async Task TransitionOrderState(string orderId, string transition)
    {
        var body = new JsonObject { ["sendMail"] = false };
        await Send(HttpMethod.Post, $"/api/_action/order/{orderId}/state/{transition}", body);
    }

    public async Task TransitionTransactionState(string transactionId, string transition)
    {
        var body = new JsonObject { ["sendMail"] = false };
        await Send(HttpMethod.Post, $"/api/_action/order_transaction/{transactionId}/state/{transition}", body);
    }

    public async Task TransitionDeliveryState(string deliveryId, string transition)
    {
        var body = new JsonObject { ["sendMail"] = false };
        await Send(HttpMethod.Post, $"/api/_action/order_delivery/{deliveryId}/state/{transition}", body);
    }

    public async Task ActivateCustomer(string customerId)
    {
        var body = new JsonObject
        {
            ["active"] = true,
            ["doubleOptInConfirmDate"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.000+00:00")
        };

        await Send(HttpMethod.Patch, $"/api/customer/{customerId}", body);
    }

}
