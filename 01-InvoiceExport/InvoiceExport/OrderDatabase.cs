namespace InvoiceExport;

public sealed class OrderDatabase
{
    readonly List<Order> _pending =
    [
        new(1001, 1, 240.00m, "Zooba"),
        new(1002, 2, 180.50m, "Koshary El Tahrir"),
        new(1003, 3, 95.00m, "Hawawshi House"),
        new(1004, 1, 310.00m, "Zooba"),
        new(1005, 4, 150.00m, "Fattah Street"),
    ];

    public List<Order> GetPendingOrders()
    {
        Thread.Sleep(800);
        return _pending.ToList();
    }

   
}
