namespace ShopwareDataHydrator.Models;

public class AppConfig
{
    public string Url { get; set; } = "";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public int CustomerCount { get; set; } = 10;
    public int OrderCount { get; set; } = 50;
    public int Days { get; set; } = 365;
    public int FailedOrdersPercent { get; set; } = 5;
    public string SalesChannel { get; set; } = "";
    public int LiveDuration { get; set; }
    public bool LiveStatus { get; set; }
}
