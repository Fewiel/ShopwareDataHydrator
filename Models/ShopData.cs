namespace ShopwareDataHydrator.Models;

public class ShopData
{
    public SalesChannelInfo SalesChannel { get; set; } = new();
    public List<SalutationInfo> Salutations { get; set; } = [];
    public List<CountryInfo> Countries { get; set; } = [];
    public List<string> PromotionCodes { get; set; } = [];
    public List<PaymentMethodInfo> PaymentMethods { get; set; } = [];
    public List<ShippingMethodInfo> ShippingMethods { get; set; } = [];
}
