namespace ShopwareDataHydrator.Hydrators;

using Bogus;
using ShopwareDataHydrator.Api;
using ShopwareDataHydrator.Models;

public class CustomerHydrator
{
    private readonly StorefrontApiClient _storefront;
    private readonly AdminApiClient _admin;
    private readonly ShopData _shopData;
    private readonly Faker _faker;
    private readonly string _password;

    public CustomerHydrator(StorefrontApiClient storefront, AdminApiClient admin, ShopData shopData)
    {
        _storefront = storefront;
        _admin = admin;
        _shopData = shopData;
        _faker = new Faker("de");
        _password = GeneratePassword();
    }

    private static string GeneratePassword()
    {
        const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string special = "!@#$%^&*";
        var rng = new Random();
        var chars = new char[24];
        chars[0] = upper[rng.Next(upper.Length)];
        chars[1] = lower[rng.Next(lower.Length)];
        chars[2] = digits[rng.Next(digits.Length)];
        chars[3] = special[rng.Next(special.Length)];
        var all = upper + lower + digits + special;
        for (int i = 4; i < chars.Length; i++)
            chars[i] = all[rng.Next(all.Length)];
        rng.Shuffle(chars);
        return new string(chars);
    }

    public async Task<List<CustomerInfo>> CreateCustomers(int count)
    {
        var customers = new List<CustomerInfo>();
        var country = ResolveCountry();

        Console.WriteLine();
        Console.WriteLine($"Creating {count} customers...");

        for (int i = 0; i < count; i++)
        {
            try
            {
                var customer = await CreateSingleCustomer(country);
                customers.Add(customer);
                Console.WriteLine($"  [{i + 1}/{count}] {customer.FirstName} {customer.LastName} ({customer.Email})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{i + 1}/{count}] FAILED: {ex.Message}");
            }
        }

        Console.WriteLine($"Created {customers.Count}/{count} customers.");
        return customers;
    }

    private async Task<CustomerInfo> CreateSingleCustomer(CountryInfo country)
    {
        var isFemale = _faker.Random.Bool();
        var salutation = ResolveSalutation(isFemale);

        var firstName = isFemale
            ? _faker.Name.FirstName(Bogus.DataSets.Name.Gender.Female)
            : _faker.Name.FirstName(Bogus.DataSets.Name.Gender.Male);
        var lastName = _faker.Name.LastName();

        var email = EmailHelper.Generate(firstName, lastName, _faker.Random.AlphaNumeric(6));
        var password = _password;
        var street = $"{_faker.Address.StreetName()} {_faker.Random.Number(1, 200)}";
        var zipcode = _faker.Address.ZipCode();
        var city = _faker.Address.City();
        var storefrontUrl = GetStorefrontUrl();

        var (customerId, _) = await _storefront.RegisterCustomer(
            salutation.Id, firstName, lastName, email, password,
            street, zipcode, city, country.Id, storefrontUrl);

        if (!string.IsNullOrEmpty(customerId))
        {
            try { await _admin.ActivateCustomer(customerId); }
            catch { }
        }

        return new CustomerInfo
        {
            Id = customerId,
            Email = email,
            Password = password,
            FirstName = firstName,
            LastName = lastName,
            SalutationId = salutation.Id
        };
    }

    private SalutationInfo ResolveSalutation(bool isFemale)
    {
        var key = isFemale ? "mrs" : "mr";
        return _shopData.Salutations.FirstOrDefault(s => s.SalutationKey == key)
               ?? _shopData.Salutations.First();
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

    private string GetStorefrontUrl()
    {
        return !string.IsNullOrEmpty(_shopData.SalesChannel.DomainUrl)
            ? _shopData.SalesChannel.DomainUrl.TrimEnd('/')
            : "";
    }
}
