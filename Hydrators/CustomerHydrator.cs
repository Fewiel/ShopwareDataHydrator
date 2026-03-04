namespace ShopwareDataHydrator.Hydrators;

using Bogus;
using ShopwareDataHydrator.Api;
using ShopwareDataHydrator.Models;

public class CustomerHydrator
{
    private readonly StorefrontApiClient _storefront;
    private readonly AdminApiClient _admin;
    private readonly ShopData _shopData;
    private readonly string _password;
    private readonly Random _random = new();

    public CustomerHydrator(StorefrontApiClient storefront, AdminApiClient admin, ShopData shopData)
    {
        _storefront = storefront;
        _admin = admin;
        _shopData = shopData;
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

        Console.WriteLine();
        Console.WriteLine($"Creating {count} customers...");

        for (int i = 0; i < count; i++)
        {
            try
            {
                var country = _shopData.Countries[_random.Next(_shopData.Countries.Count)];
                var customer = await CreateSingleCustomer(country);
                customers.Add(customer);
                Console.WriteLine($"  [{i + 1}/{count}] {customer.FirstName} {customer.LastName} ({customer.Email}) [{country.Iso}]");
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
        var locale = LocaleHelper.GetBogusLocale(country.Iso);
        var faker = new Faker(locale);

        var isFemale = faker.Random.Bool();
        var salutation = ResolveSalutation(isFemale);

        var firstName = isFemale
            ? faker.Name.FirstName(Bogus.DataSets.Name.Gender.Female)
            : faker.Name.FirstName(Bogus.DataSets.Name.Gender.Male);
        var lastName = faker.Name.LastName();

        var email = EmailHelper.Generate(firstName, lastName, faker.Random.AlphaNumeric(6));
        var password = _password;
        var street = $"{faker.Address.StreetName()} {faker.Random.Number(1, 200)}";
        var zipcode = faker.Address.ZipCode();
        var city = faker.Address.City();
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

    public (string FirstName, string LastName, string Street, string Zipcode, string City, SalutationInfo Salutation) GeneratePersonData(CountryInfo country)
    {
        var locale = LocaleHelper.GetBogusLocale(country.Iso);
        var faker = new Faker(locale);
        var isFemale = faker.Random.Bool();
        var salutation = ResolveSalutation(isFemale);
        var firstName = isFemale
            ? faker.Name.FirstName(Bogus.DataSets.Name.Gender.Female)
            : faker.Name.FirstName(Bogus.DataSets.Name.Gender.Male);
        var lastName = faker.Name.LastName();
        var street = $"{faker.Address.StreetName()} {faker.Random.Number(1, 200)}";
        var zipcode = faker.Address.ZipCode();
        var city = faker.Address.City();
        return (firstName, lastName, street, zipcode, city, salutation);
    }

    private SalutationInfo ResolveSalutation(bool isFemale)
    {
        var key = isFemale ? "mrs" : "mr";
        return _shopData.Salutations.FirstOrDefault(s => s.SalutationKey == key)
               ?? _shopData.Salutations.First();
    }

    private string GetStorefrontUrl()
    {
        return !string.IsNullOrEmpty(_shopData.SalesChannel.DomainUrl)
            ? _shopData.SalesChannel.DomainUrl.TrimEnd('/')
            : "";
    }
}
