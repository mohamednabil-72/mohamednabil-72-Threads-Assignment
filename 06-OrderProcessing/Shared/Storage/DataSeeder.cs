namespace OrderProcessing.Shared.Storage;

public static class DataSeeder
{
    public static async Task ResetAsync(JsonFileStore store, CancellationToken ct)
    {
        try
        {
            await ResetCoreAsync(store, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A cancelled reset must not leave a partially recreated Data folder.
            // Complete the reset without the cancelled token, then propagate cancellation.
            await ResetCoreAsync(store, CancellationToken.None);
            throw;
        }
    }

    private static async Task ResetCoreAsync(JsonFileStore store, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        store.ResetDirectory(DataFiles.InvoicesFolder, DataFiles.ReportsFolder);
        await SeedAsync(store, DataFiles.Customers, BuildCustomers(), ct);
        await SeedAsync(store, DataFiles.Products, BuildProducts(), ct);
        await SeedAsync(store, DataFiles.Coupons, BuildCoupons(), ct);
        await SeedAsync(store, DataFiles.IncomingOrders, BuildIncomingOrders(), ct);
        await SeedAsync(store, DataFiles.Orders, new List<Order>(), ct);
        await SeedAsync(store, DataFiles.Payments, new List<PaymentTransaction>(), ct);
        await SeedAsync(store, DataFiles.Emails, new List<EmailMessage>(), ct);
        await SeedAsync(store, DataFiles.EmailRetry, new List<EmailMessage>(), ct);
    }

    private static async Task SeedAsync<T>(
        JsonFileStore store,
        string fileName,
        List<T> records,
        CancellationToken ct)
    {
        await store.WriteAllAsync(fileName, records, ct);
        Log.Info("Seeder", $"{fileName} -> {records.Count} records");
    }

    private static List<Customer> BuildCustomers()
    {
        return new List<Customer>
        {
            NewCustomer(1, "Ahmed Ali", true, LoyaltyTier.Gold),
            NewCustomer(2, "Sara Mohamed", true, LoyaltyTier.Silver),
            NewCustomer(3, "Omar Hassan", true, LoyaltyTier.Bronze),
            NewCustomer(4, "Mona Ibrahim", false, LoyaltyTier.Silver),
            NewCustomer(5, "Youssef Adel", true, LoyaltyTier.Gold),
            NewCustomer(6, "Nour Khaled", true, LoyaltyTier.Bronze),
            NewCustomer(7, "Karim Samir", true, LoyaltyTier.Silver),
            NewCustomer(8, "Laila Mahmoud", true, LoyaltyTier.Gold),
            NewCustomer(9, "Hassan Tarek", true, LoyaltyTier.Bronze),
            NewCustomer(10, "Dina Fathy", true, LoyaltyTier.Silver)
        };
    }

    private static Customer NewCustomer(int id, string name, bool isActive, LoyaltyTier tier)
    {
        return new Customer
        {
            Id = id,
            Name = name,
            Email = name.ToLowerInvariant().Replace(' ', '.') + "@example.com",
            IsActive = isActive,
            LoyaltyTier = tier
        };
    }

    private static List<Product> BuildProducts()
    {
        return new List<Product>
        {
            NewProduct(1, "Laptop", 1200.00m, 10, 2.2m),
            NewProduct(2, "Smartphone", 800.00m, 15, 0.4m),
            NewProduct(3, "Wireless Mouse", 25.00m, 2, 0.1m),
            NewProduct(4, "Mechanical Keyboard", 90.00m, 1, 0.9m),
            NewProduct(5, "USB-C Hub", 45.00m, 30, 0.2m),
            NewProduct(6, "Headphones", 150.00m, 12, 0.3m),
            NewProduct(7, "27\" Monitor", 350.00m, 6, 5.0m),
            NewProduct(8, "Webcam", 70.00m, 20, 0.2m)
        };
    }

    private static Product NewProduct(int id, string name, decimal price, int stock, decimal weightKg)
    {
        return new Product
        {
            Id = id,
            Name = name,
            Price = price,
            StockQuantity = stock,
            WeightKg = weightKg
        };
    }

    private static List<Coupon> BuildCoupons()
    {
        DateTime today = DateTime.Today;

        return new List<Coupon>
        {
            new() { Code = "SAVE10", DiscountPercent = 10, ExpiryDate = today.AddDays(180) },
            new() { Code = "WELCOME15", DiscountPercent = 15, ExpiryDate = today.AddDays(90) },
            new() { Code = "FLASH20", DiscountPercent = 20, ExpiryDate = today.AddDays(7) },
            new() { Code = "OLD20", DiscountPercent = 20, ExpiryDate = today.AddDays(-30) }
        };
    }

    private static List<Order> BuildIncomingOrders()
    {
        DateTime now = DateTime.Now;

        Order Make(int id, int customerId, string? coupon, params (int ProductId, int Quantity)[] items)
        {
            return new Order
            {
                Id = id,
                CustomerId = customerId,
                CouponCode = coupon,
                Items = items
                    .Select(i => new OrderItem { ProductId = i.ProductId, Quantity = i.Quantity, UnitPrice = 0m })
                    .ToList(),
                SubTotal = 0m,
                Discount = 0m,
                ShippingCost = 0m,
                TotalAmount = 0m,
                Status = OrderStatus.Pending,
                CreatedAt = now,
                FailureReason = null,
                TransactionId = null
            };
        }

        return new List<Order>
        {
            Make(1, 1, "SAVE10", (1, 1), (3, 1)),
            Make(2, 2, null, (2, 1)),
            Make(3, 3, "WELCOME15", (5, 2), (8, 1)),
            Make(4, 4, null, (6, 1)),
            Make(5, 5, null, (7, 1)),
            Make(6, 6, "FLASH20", (5, 1)),
            Make(7, 7, null, (6, 1), (8, 1)),
            Make(8, 8, "SAVE10", (1, 1)),
            Make(9, 9, null, (3, 5)),
            Make(10, 10, null, (4, 1)),
            Make(11, 1, null, (4, 1)),
            Make(12, 2, "OLD20", (2, 1)),
            Make(13, 3, null, (6, 2)),
            Make(14, 5, "WELCOME15", (8, 2), (5, 1)),
            Make(15, 99, null, (2, 1)),
            Make(16, 6, null, (7, 1), (5, 1)),
            Make(17, 7, "SAVE10", (2, 1)),
            Make(18, 8, "FAKE50", (6, 1)),
            Make(19, 9, null, (8, 1)),
            Make(20, 10, "FLASH20", (1, 1), (6, 1))
        };
    }
}
