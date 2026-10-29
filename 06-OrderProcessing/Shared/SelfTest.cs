using System.Diagnostics;

namespace OrderProcessing;

public static class SelfTest
{
    private const string Source = "SelfTest";

    private static int _failed;

    private sealed class Poison
    {
        public int Id => 1;

        public string Boom => throw new InvalidOperationException("simulated crash while writing");
    }

    public static async Task<bool> RunAsync(JsonFileStore store, CancellationToken ct)
    {
        _failed = 0;

        await DataSeeder.ResetAsync(store, ct);

        await CheckSeedAsync(store, ct);
        await CheckMissingAndCorruptedAsync(store, ct);
        await CheckConcurrentAccessAsync(store, ct);
        await CheckAtomicWriteAsync(store, ct);
        await CheckLatencyAndSaveAsync(store, ct);
        await CheckStockAsync(store, ct);
        await CheckCancellationAsync(store, ct);
        CheckRunSummary();

        foreach (string file in DataFiles.All)
        {
            await store.CountRecordsAsync(file, ct);
        }

        Check("every data file is valid JSON at the end", true);

        await DataSeeder.ResetAsync(store, ct);

        if (_failed == 0)
        {
            Log.Success(Source, "All checks passed");
        }
        else
        {
            Log.Error(Source, $"{_failed} check(s) failed");
        }

        return _failed == 0;
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        string text = detail.Length == 0 ? name : $"{name} ({detail})";

        if (ok)
        {
            Log.Success(Source, $"PASS {text}");
        }
        else
        {
            Interlocked.Increment(ref _failed);
            Log.Error(Source, $"FAIL {text}");
        }
    }

    private static async Task CheckSeedAsync(JsonFileStore store, CancellationToken ct)
    {
        var expected = new (string File, int Count)[]
        {
            (DataFiles.Customers, 10),
            (DataFiles.Products, 8),
            (DataFiles.Coupons, 4),
            (DataFiles.IncomingOrders, 20),
            (DataFiles.Orders, 0),
            (DataFiles.Payments, 0),
            (DataFiles.Emails, 0),
            (DataFiles.EmailRetry, 0)
        };

        foreach ((string file, int count) in expected)
        {
            int actual = await store.CountRecordsAsync(file, ct);
            Check($"{file} holds {count} records", actual == count, $"found {actual}");
        }

        List<Customer> customers = await store.ReadAllAsync<Customer>(DataFiles.Customers, ct);
        Check("customer emails are lower-case firstname.lastname@example.com",
            customers[0].Email == "ahmed.ali@example.com" && customers[3].IsActive == false);

        List<Product> products = await store.ReadAllAsync<Product>(DataFiles.Products, ct);
        Check("seeded stock matches the specification",
            products[2].StockQuantity == 2 && products[3].StockQuantity == 1 && products[6].Name == "27\" Monitor");

        List<Coupon> coupons = await store.ReadAllAsync<Coupon>(DataFiles.Coupons, ct);
        Check("coupon dates are relative to today and OLD20 is expired",
            coupons[0].ExpiryDate == DateTime.Today.AddDays(180) &&
            coupons[2].ExpiryDate == DateTime.Today.AddDays(7) &&
            coupons[3].ExpiryDate == DateTime.Today.AddDays(-30));

        List<Order> incoming = await store.ReadAllAsync<Order>(DataFiles.IncomingOrders, ct);
        Check("incoming orders start Pending with unit price 0",
            incoming.Count == 20 &&
            incoming.All(o => o.Status == OrderStatus.Pending && o.Items.All(i => i.UnitPrice == 0m)) &&
            incoming[0].CouponCode == "SAVE10" && incoming[0].Items.Count == 2 &&
            incoming[14].CustomerId == 99);

        string raw = await store.ReadRawTextAsync(DataFiles.Customers, ct);
        Check("enums are stored as text", raw.Contains("\"loyaltyTier\": \"Gold\""));

        string empty = (await store.ReadRawTextAsync(DataFiles.Orders, ct)).Trim();
        Check("an empty collection is stored as []", empty == "[]");
    }

    private static async Task CheckMissingAndCorruptedAsync(JsonFileStore store, CancellationToken ct)
    {
        const string missing = "_selftest_missing.json";
        List<Customer> none = await store.ReadAllAsync<Customer>(missing, ct);
        Check("reading a missing file returns an empty collection", none.Count == 0);

        const string corrupt = "_selftest_corrupt.json";
        try
        {
            foreach (string content in new[] { "{ not json", "", "null", "{}", "123", "\"text\"", "true" })
            {
                await store.WriteRawTextAsync(corrupt, content, ct);

                bool threw = false;
                bool namesFile = false;

                try
                {
                    await store.ReadAllAsync<Customer>(corrupt, ct);
                }
                catch (DataFileCorruptedException ex)
                {
                    threw = true;
                    namesFile = ex.Message.Contains(corrupt);
                }

                Check($"invalid content '{content}' throws DataFileCorruptedException naming the file", threw && namesFile);
            }
        }
        finally
        {
            await store.DeleteAsync(corrupt, ct);
        }
    }

    private static async Task CheckConcurrentAccessAsync(JsonFileStore store, CancellationToken ct)
    {
        const string file = "_selftest_concurrent.json";

        try
        {
            IEnumerable<Task> asyncWriters = Enumerable.Range(1, 200)
                .Select(i => Task.Run(() => store.AppendAsync(file, new Order { Id = i }, ct), ct));

            IEnumerable<Task> syncWriters = Enumerable.Range(1001, 50)
                .Select(i => Task.Run(() => store.Append(file, new Order { Id = i }), ct));

            IEnumerable<Task> readers = Enumerable.Range(0, 100)
                .Select(_ => Task.Run(() => store.ReadAllAsync<Order>(file, ct), ct));

            bool noErrors = true;

            try
            {
                await Task.WhenAll(asyncWriters.Concat(syncWriters).Concat(readers));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                noErrors = false;
            }

            List<Order> all = await store.ReadAllAsync<Order>(file, ct);
            int distinct = all.Select(o => o.Id).Distinct().Count();

            Check("readers never see a corrupted file while writers run", noErrors);
            Check("250 concurrent async and sync appends lose no update", all.Count == 250 && distinct == 250,
                $"{all.Count} records, {distinct} distinct");
        }
        finally
        {
            await store.DeleteAsync(file, ct);
        }
    }

    private static async Task CheckAtomicWriteAsync(JsonFileStore store, CancellationToken ct)
    {
        const string file = "_selftest_atomic.json";

        try
        {
            await store.WriteAllAsync(file, new[] { new Order { Id = 1 }, new Order { Id = 2 }, new Order { Id = 3 } }, ct);

            bool crashed = false;

            try
            {
                await store.WriteAllAsync(file, new[] { new Poison(), new Poison() }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                crashed = true;
            }

            List<Order> after = await store.ReadAllAsync<Order>(file, ct);
            Check("a crash in the middle of a write leaves the previous file intact", crashed && after.Select(o => o.Id).SequenceEqual(new[] { 1, 2, 3 }));

            bool leftovers = Directory.EnumerateFiles(store.DirectoryPath, "*.tmp", SearchOption.AllDirectories).Any();
            Check("no temporary files are left behind", !leftovers);
        }
        finally
        {
            await store.DeleteAsync(file, ct);
        }
    }

    private static async Task CheckLatencyAndSaveAsync(JsonFileStore store, CancellationToken ct)
    {
        var customers = new CustomerRepository(store);
        var orders = new OrderRepository(store);

        var probes = new (string Name, int Min, int Max, Task<long> Elapsed)[]
        {
            ("DB query", 500, 800, TimeAsync(() => customers.GetByIdAsync(3, ct))),
            ("DB write", 900, 900, TimeAsync(() => orders.SaveAsync(
                new Order { Id = 1, CustomerId = 3, Status = OrderStatus.Pending }, ct))),
            ("External API", 950, 1950, TimeAsync(() => Latency.ExternalApiAsync(Source, "latency probe", ct))),
            ("Payment gateway", 2000, 2000, TimeAsync(() => Latency.PaymentGatewayAsync(Source, "latency probe", ct))),
            ("Email", 1000, 1000, TimeAsync(() => Latency.SmtpAsync(Source, "latency probe", ct)))
        };

        await Task.WhenAll(probes.Select(p => p.Elapsed));

        foreach (var probe in probes)
        {
            long ms = await probe.Elapsed;
            Check($"{probe.Name} latency is {probe.Min}-{probe.Max} ms", ms >= probe.Min - 30 && ms <= probe.Max + 600,
                $"{ms} ms");
        }

        await orders.SaveAsync(new Order { Id = 1, CustomerId = 3, Status = OrderStatus.Completed }, ct);
        List<Order> saved = await store.ReadAllAsync<Order>(DataFiles.Orders, ct);
        Check("saving an order twice updates it instead of duplicating it",
            saved.Count == 1 && saved[0].Status == OrderStatus.Completed);
    }

    private static async Task CheckStockAsync(JsonFileStore store, CancellationToken ct)
    {
        var products = new ProductRepository(store);
        var keyboard = new List<OrderItem> { new() { ProductId = 4, Quantity = 1 } };

        bool[] race = await Task.WhenAll(
            products.ReserveStockAsync(keyboard, ct),
            products.ReserveStockAsync(keyboard, ct));

        Check("two orders racing for the last keyboard: exactly one wins", race.Count(r => r) == 1);
        Check("the losing order does not drive stock negative", await StockOfAsync(store, 4, ct) == 0);

        await products.ReleaseStockAsync(keyboard, ct);
        Check("releasing the stock restores it", await StockOfAsync(store, 4, ct) == 1);

        var mixed = new List<OrderItem>
        {
            new() { ProductId = 4, Quantity = 1 },
            new() { ProductId = 3, Quantity = 5 }
        };

        bool reserved = await products.ReserveStockAsync(mixed, ct);
        Check("a reservation is all-or-nothing",
            !reserved && await StockOfAsync(store, 4, ct) == 1 && await StockOfAsync(store, 3, ct) == 2);
    }

    private static async Task CheckCancellationAsync(JsonFileStore store, CancellationToken ct)
    {
        var products = new ProductRepository(store);
        var orders = new OrderRepository(store);
        var keyboard = new List<OrderItem> { new() { ProductId = 4, Quantity = 1 } };

        bool early = await CancelsAsync(t => products.ReserveStockAsync(keyboard, t), 100, ct);
        Check("cancelling during the connection step stops the reservation",
            early && await StockOfAsync(store, 4, ct) == 1);

        bool late = await CancelsAsync(t => products.ReserveStockAsync(keyboard, t), 800, ct);
        Check("cancelling during commit rolls the reservation back",
            late && await StockOfAsync(store, 4, ct) == 1);

        bool saveCancelled = await CancelsAsync(
            t => orders.SaveAsync(new Order { Id = 2, CustomerId = 1, Status = OrderStatus.Pending }, t), 800, ct);
        List<Order> saved = await store.ReadAllAsync<Order>(DataFiles.Orders, ct);
        Check("cancelling during commit rolls an order save back", saveCancelled && saved.Count == 1);

        foreach (string file in DataFiles.All)
        {
            await store.CountRecordsAsync(file, ct);
        }

        Check("every data file is valid JSON after cancelled operations", true);
    }

    private static void CheckRunSummary()
    {
        RunSummary.Reset();
        var summary = new RunSummary();

        Parallel.For(0, 1000, i =>
        {
            if (i % 4 == 0)
            {
                summary.OrderCancelled();
            }
            else
            {
                summary.OrderFinished();
            }
        });

        Check("run summary counts concurrently without losing increments",
            summary.Finished == 750 && summary.Cancelled == 250);
        Check("cancelled run message has the required format",
            summary.CancelledMessage() == "Run cancelled — 750 orders finished, 250 cancelled");
    }

    private static async Task<bool> CancelsAsync(Func<CancellationToken, Task> action, int cancelAfterMs, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(cancelAfterMs);

        try
        {
            await action(cts.Token);
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return true;
        }
    }

    private static async Task<long> TimeAsync(Func<Task> action)
    {
        var stopwatch = Stopwatch.StartNew();
        await action();
        return stopwatch.ElapsedMilliseconds;
    }

    private static async Task<int> StockOfAsync(JsonFileStore store, int productId, CancellationToken ct)
    {
        List<Product> products = await store.ReadAllAsync<Product>(DataFiles.Products, ct);
        return products.First(p => p.Id == productId).StockQuantity;
    }
}
