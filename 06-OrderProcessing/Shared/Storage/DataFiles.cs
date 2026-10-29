namespace OrderProcessing.Shared.Storage;

public static class DataFiles
{
    public const string Customers = "customers.json";
    public const string Products = "products.json";
    public const string Coupons = "coupons.json";
    public const string IncomingOrders = "incoming_orders.json";
    public const string Orders = "orders.json";
    public const string Payments = "payments.json";
    public const string Emails = "emails.json";
    public const string EmailRetry = "email_retry.json";

    public const string InvoicesFolder = "invoices";
    public const string ReportsFolder = "reports";

    public static readonly string DirectoryPath = Path.Combine(AppContext.BaseDirectory, "Data");

    public static readonly string[] All =
    {
        Customers,
        Products,
        Coupons,
        IncomingOrders,
        Orders,
        Payments,
        Emails,
        EmailRetry
    };
}
