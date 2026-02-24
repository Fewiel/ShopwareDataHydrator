namespace ShopwareDataHydrator.Hydrators;

using Bogus;
using ShopwareDataHydrator.Api;
using ShopwareDataHydrator.Models;

public enum OrderStatus
{
    Open,
    InProgress,
    Completed,
    Cancelled,
    Refunded
}

public class OrderHydrator
{
    private readonly StorefrontApiClient _storefront;
    private readonly AdminApiClient _admin;
    private readonly ShopData _shopData;
    private readonly List<ProductInfo> _products;
    private readonly List<CustomerInfo> _customers;
    private readonly AppConfig _config;
    private readonly Faker _faker;
    private readonly Random _random;
    private readonly Dictionary<string, string> _customerTokens = new();

    public OrderHydrator(
        StorefrontApiClient storefront, AdminApiClient admin,
        ShopData shopData, List<ProductInfo> products, List<CustomerInfo> customers,
        AppConfig config)
    {
        _storefront = storefront;
        _admin = admin;
        _shopData = shopData;
        _products = products;
        _customers = customers;
        _config = config;
        _faker = new Faker("de");
        _random = new Random();
    }

    public async Task CreateOrders(int count)
    {
        Console.WriteLine();
        await PreAuthenticateCustomers();

        Console.WriteLine();
        Console.WriteLine($"Creating {count} orders...");

        var guestRatio = 0.3;
        var promoRatio = 0.2;
        var created = new List<(string OrderId, OrderStatus Status, DateTimeOffset Date)>();

        var orderPlan = BuildOrderPlan(count);

        for (int i = 0; i < count; i++)
        {
            var isGuest = _random.NextDouble() < guestRatio || _customers.Count == 0;
            var (targetStatus, targetDate) = orderPlan[i];
            var usePromo = _shopData.PromotionCodes.Count > 0 && _random.NextDouble() < promoRatio;

            try
            {
                string? orderId;
                string label;

                if (isGuest)
                {
                    (orderId, label) = await PlaceGuestOrder(usePromo);
                }
                else
                {
                    var customer = _customers[_random.Next(_customers.Count)];
                    (orderId, label) = await PlaceCustomerOrder(customer, usePromo);
                }

                if (!string.IsNullOrEmpty(orderId))
                {
                    created.Add((orderId, targetStatus, targetDate));
                    Console.WriteLine($"  [{i + 1}/{count}] {label} | {targetStatus} | {targetDate:yyyy-MM-dd}");
                }
                else
                {
                    Console.WriteLine($"  [{i + 1}/{count}] FAILED: No order ID returned");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{i + 1}/{count}] FAILED: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Processing {created.Count} order dates and statuses...");

        for (int i = 0; i < created.Count; i++)
        {
            var (orderId, status, date) = created[i];
            try
            {
                await _admin.UpdateOrderDate(orderId, date);
                await ApplyOrderStatus(orderId, status);
                Console.WriteLine($"  [{i + 1}/{created.Count}] {status} | {date:yyyy-MM-dd}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{i + 1}/{created.Count}] Status update failed: {ex.Message}");
            }
        }

        Console.WriteLine($"Processed {created.Count} orders.");
    }

    public async Task CreateOrdersLive(int count, int durationSeconds, bool applyStatus)
    {
        Console.WriteLine();
        await PreAuthenticateCustomers();

        var intervalMs = (int)((double)durationSeconds / count * 1000);
        Console.WriteLine();
        Console.WriteLine($"Live mode: {count} orders over {durationSeconds}s (~{intervalMs / 1000.0:F1}s between orders)");
        Console.WriteLine();

        var guestRatio = 0.3;
        var promoRatio = 0.2;
        var statusPlan = applyStatus ? BuildStatusPlan(count) : null;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < count; i++)
        {
            var isGuest = _random.NextDouble() < guestRatio || _customers.Count == 0;
            var usePromo = _shopData.PromotionCodes.Count > 0 && _random.NextDouble() < promoRatio;

            try
            {
                string? orderId;
                string label;

                if (isGuest)
                    (orderId, label) = await PlaceGuestOrder(usePromo);
                else
                {
                    var customer = _customers[_random.Next(_customers.Count)];
                    (orderId, label) = await PlaceCustomerOrder(customer, usePromo);
                }

                if (!string.IsNullOrEmpty(orderId) && applyStatus && statusPlan != null)
                {
                    var status = statusPlan[i];
                    await ApplyOrderStatus(orderId, status);
                    Console.WriteLine($"  [{i + 1}/{count}] {label} | {status} | {stopwatch.Elapsed:mm\\:ss}");
                }
                else if (!string.IsNullOrEmpty(orderId))
                {
                    Console.WriteLine($"  [{i + 1}/{count}] {label} | Open | {stopwatch.Elapsed:mm\\:ss}");
                }
                else
                {
                    Console.WriteLine($"  [{i + 1}/{count}] FAILED: No order ID returned");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{i + 1}/{count}] FAILED: {ex.Message}");
            }

            if (i < count - 1)
            {
                var elapsed = stopwatch.ElapsedMilliseconds;
                var expectedMs = (long)(i + 1) * intervalMs;
                var waitMs = expectedMs - elapsed;
                if (waitMs > 0)
                    await Task.Delay((int)waitMs);
            }
        }

        Console.WriteLine($"Live mode completed in {stopwatch.Elapsed:mm\\:ss}.");
    }

    private async Task PreAuthenticateCustomers()
    {
        if (_customers.Count == 0) return;

        Console.WriteLine($"Pre-authenticating {_customers.Count} customers...");
        foreach (var customer in _customers)
        {
            try
            {
                var token = await _storefront.Login(customer.Email, customer.Password);
                _customerTokens[customer.Email] = token;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Login failed for {customer.Email}: {ex.Message}");
            }
        }
        Console.WriteLine($"Authenticated {_customerTokens.Count}/{_customers.Count} customers.");
    }

    private List<OrderStatus> BuildStatusPlan(int count)
    {
        var failedCount = (int)Math.Round(count * _config.FailedOrdersPercent / 100.0);
        var cancelledCount = failedCount / 2;
        var refundedCount = failedCount - cancelledCount;
        var remaining = count - failedCount;
        var completedCount = (int)Math.Round(remaining * 0.65);
        var inProgressCount = remaining - completedCount;

        var plan = new List<OrderStatus>();
        for (int i = 0; i < cancelledCount; i++) plan.Add(OrderStatus.Cancelled);
        for (int i = 0; i < refundedCount; i++) plan.Add(OrderStatus.Refunded);
        for (int i = 0; i < completedCount; i++) plan.Add(OrderStatus.Completed);
        for (int i = 0; i < inProgressCount; i++) plan.Add(OrderStatus.InProgress);

        return plan.OrderBy(_ => _random.Next()).ToList();
    }

    private async Task<(string? OrderId, string Label)> PlaceCustomerOrder(CustomerInfo customer, bool usePromo)
    {
        if (!_customerTokens.TryGetValue(customer.Email, out var contextToken))
            contextToken = await _storefront.Login(customer.Email, customer.Password);

        var products = SelectRandomProducts();

        contextToken = await _storefront.AddProducts(contextToken, products);

        if (usePromo)
        {
            var code = _shopData.PromotionCodes[_random.Next(_shopData.PromotionCodes.Count)];
            contextToken = await _storefront.TryAddPromotionCode(contextToken, code);
        }

        var orderId = await _storefront.PlaceOrder(contextToken);
        _customerTokens[customer.Email] = contextToken;
        return (orderId, $"{customer.FirstName} {customer.LastName} ({products.Count} products)");
    }

    private async Task<(string? OrderId, string Label)> PlaceGuestOrder(bool usePromo)
    {
        var isFemale = _faker.Random.Bool();
        var salutation = isFemale
            ? (_shopData.Salutations.FirstOrDefault(s => s.SalutationKey == "mrs") ?? _shopData.Salutations.First())
            : (_shopData.Salutations.FirstOrDefault(s => s.SalutationKey == "mr") ?? _shopData.Salutations.First());

        var firstName = isFemale
            ? _faker.Name.FirstName(Bogus.DataSets.Name.Gender.Female)
            : _faker.Name.FirstName(Bogus.DataSets.Name.Gender.Male);
        var lastName = _faker.Name.LastName();
        var email = EmailHelper.Generate("guest", Guid.NewGuid().ToString("N")[..8], "");

        var country = ResolveCountry();
        var street = $"{_faker.Address.StreetName()} {_faker.Random.Number(1, 200)}";
        var zipcode = _faker.Address.ZipCode();
        var city = _faker.Address.City();
        var storefrontUrl = !string.IsNullOrEmpty(_shopData.SalesChannel.DomainUrl)
            ? _shopData.SalesChannel.DomainUrl.TrimEnd('/')
            : "";

        var (_, contextToken) = await _storefront.RegisterCustomer(
            salutation.Id, firstName, lastName, email, "",
            street, zipcode, city, country.Id, storefrontUrl, guest: true);

        var products = SelectRandomProducts();
        contextToken = await _storefront.AddProducts(contextToken, products);

        if (usePromo)
        {
            var code = _shopData.PromotionCodes[_random.Next(_shopData.PromotionCodes.Count)];
            contextToken = await _storefront.TryAddPromotionCode(contextToken, code);
        }

        var orderId = await _storefront.PlaceOrder(contextToken);
        return (orderId, $"Guest: {firstName} {lastName} ({products.Count} products)");
    }

    private List<(string ProductId, int Quantity)> SelectRandomProducts()
    {
        var productCount = _random.Next(1, Math.Min(5, _products.Count) + 1);
        return _products
            .OrderBy(_ => _random.Next())
            .Take(productCount)
            .Select(p => (p.Id, _random.Next(1, 4)))
            .ToList();
    }

    private List<(OrderStatus Status, DateTimeOffset Date)> BuildOrderPlan(int count)
    {
        var now = DateTimeOffset.UtcNow;
        var rangeStart = now.AddDays(-_config.Days);
        var openZoneDays = Math.Max(1.0, _config.Days * 0.05);
        var openZoneStart = now.AddDays(-openZoneDays);

        var failedCount = (int)Math.Round(count * _config.FailedOrdersPercent / 100.0);
        var cancelledCount = failedCount / 2;
        var refundedCount = failedCount - cancelledCount;

        var remaining = count - failedCount;
        var openCount = (int)Math.Round(remaining * 0.05);
        if (openCount < 1 && count >= 10) openCount = 1;

        var completedCount = (int)Math.Round((remaining - openCount) * 0.65);
        var inProgressCount = remaining - openCount - completedCount;

        var plan = new List<(OrderStatus, DateTimeOffset)>();

        for (int i = 0; i < openCount; i++)
            plan.Add((OrderStatus.Open, PickWeightedDate(openZoneStart, now)));

        for (int i = 0; i < cancelledCount; i++)
            plan.Add((OrderStatus.Cancelled, PickUniformDate(rangeStart, now)));

        for (int i = 0; i < refundedCount; i++)
            plan.Add((OrderStatus.Refunded, PickUniformDate(rangeStart, now)));

        for (int i = 0; i < completedCount; i++)
            plan.Add((OrderStatus.Completed, PickUniformDate(rangeStart, now)));

        for (int i = 0; i < inProgressCount; i++)
            plan.Add((OrderStatus.InProgress, PickUniformDate(rangeStart, now)));

        Console.WriteLine($"  Plan: {openCount} Open, {completedCount} Completed, " +
                          $"{inProgressCount} InProgress, {cancelledCount} Cancelled, {refundedCount} Refunded");
        Console.WriteLine($"  Open zone: last {openZoneDays:F0} days ({openZoneStart:yyyy-MM-dd} to {now:yyyy-MM-dd})");

        return plan.OrderBy(_ => _random.Next()).ToList();
    }

    private DateTimeOffset PickWeightedDate(DateTimeOffset start, DateTimeOffset end)
    {
        var totalMinutes = (end - start).TotalMinutes;
        return start.AddMinutes(Math.Sqrt(_random.NextDouble()) * totalMinutes);
    }

    private DateTimeOffset PickUniformDate(DateTimeOffset start, DateTimeOffset end)
    {
        var totalMinutes = (end - start).TotalMinutes;
        return start.AddMinutes(_random.NextDouble() * totalMinutes);
    }

    private CountryInfo ResolveCountry()
    {
        if (!string.IsNullOrEmpty(_shopData.SalesChannel.CountryId))
        {
            var scCountry = _shopData.Countries
                .FirstOrDefault(c => c.Id == _shopData.SalesChannel.CountryId);
            if (scCountry != null) return scCountry;
        }

        return _shopData.Countries.FirstOrDefault(c => c.Iso == "DE")
               ?? _shopData.Countries.First();
    }

    private async Task ApplyOrderStatus(string orderId, OrderStatus status)
    {
        if (status == OrderStatus.Open)
            return;

        var details = await _admin.GetOrderDetails(orderId);

        switch (status)
        {
            case OrderStatus.InProgress:
                await _admin.TransitionOrderState(orderId, "process");
                if (!string.IsNullOrEmpty(details.TransactionId))
                    await SafeTransition(() => _admin.TransitionTransactionState(details.TransactionId, "paid"));
                break;

            case OrderStatus.Completed:
                await _admin.TransitionOrderState(orderId, "process");
                await _admin.TransitionOrderState(orderId, "complete");
                if (!string.IsNullOrEmpty(details.TransactionId))
                    await SafeTransition(() => _admin.TransitionTransactionState(details.TransactionId, "paid"));
                if (!string.IsNullOrEmpty(details.DeliveryId))
                    await SafeTransition(() => _admin.TransitionDeliveryState(details.DeliveryId, "ship"));
                break;

            case OrderStatus.Cancelled:
                await _admin.TransitionOrderState(orderId, "cancel");
                if (!string.IsNullOrEmpty(details.TransactionId))
                    await SafeTransition(() => _admin.TransitionTransactionState(details.TransactionId, "cancel"));
                if (!string.IsNullOrEmpty(details.DeliveryId))
                    await SafeTransition(() => _admin.TransitionDeliveryState(details.DeliveryId, "cancel"));
                break;

            case OrderStatus.Refunded:
                await _admin.TransitionOrderState(orderId, "process");
                if (!string.IsNullOrEmpty(details.TransactionId))
                {
                    await SafeTransition(() => _admin.TransitionTransactionState(details.TransactionId, "paid"));
                    await SafeTransition(() => _admin.TransitionTransactionState(details.TransactionId, "refund"));
                }
                await SafeTransition(() => _admin.TransitionOrderState(orderId, "cancel"));
                break;
        }
    }

    private static async Task SafeTransition(Func<Task> action)
    {
        try { await action(); }
        catch { }
    }
}
