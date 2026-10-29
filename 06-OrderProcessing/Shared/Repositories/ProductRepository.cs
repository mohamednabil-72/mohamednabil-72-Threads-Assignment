namespace OrderProcessing.Shared.Repositories;

public sealed class ProductRepository
{
    private const string Source = nameof(ProductRepository);

    private readonly JsonFileStore _store;

    public ProductRepository(JsonFileStore store)
    {
        _store = store;
    }

    public async Task<Product?> GetByIdAsync(int id, CancellationToken ct)
    {
        await Latency.QueryAsync(Source, $"Product #{id}", ct);

        List<Product> products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct);
        Product? product = products.Find(p => p.Id == id);

        Log.Info(Source, product is null
            ? $"Product #{id} not found"
            : $"Product #{id} found ({product.Name}, stock {product.StockQuantity})");

        return product;
    }

    public async Task<bool> ReserveStockUnsafeAsync(int productId, int quantity, CancellationToken ct)
    {
        List<Product> products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct);
        Product? product = products.Find(p => p.Id == productId);
        if (product is null || product.StockQuantity < quantity) return false;
        await Latency.StepAsync("UnsafeReservation", "Opening connection...", 200, ct);
        await Latency.StepAsync("UnsafeReservation", "Beginning transaction...", 100, ct);
        await Latency.StepAsync("UnsafeReservation", "Updating stock...", 400, ct);
        product.StockQuantity -= quantity;
        await _store.WriteAllAsync(DataFiles.Products, products, ct);
        return true;
    }

    public Task<bool> ReserveStockAsync(int productId, int quantity, CancellationToken ct)
        => ReserveStockAsync(new[] { new OrderItem { ProductId = productId, Quantity = quantity } }, ct);

    public Task ReleaseStockAsync(int productId, int quantity, CancellationToken ct)
        => ReleaseStockAsync(new[] { new OrderItem { ProductId = productId, Quantity = quantity } }, ct);

    public async Task<bool> ReserveStockAsync(IReadOnlyCollection<OrderItem> items, CancellationToken ct)
    {
        Dictionary<int, int> demand = Aggregate(items);
        bool reserved = false;

        await Latency.WriteAsync(
            Source,
            $"stock reservation ({Describe(demand)})",
            async token =>
            {
                reserved = await _store.UpdateAsync<Product, bool>(
                    DataFiles.Products,
                    products => TryReserve(products, demand),
                    token);
            },
            () => reserved
                ? _store.UpdateAsync<Product>(
                    DataFiles.Products,
                    products => Adjust(products, demand, 1),
                    CancellationToken.None)
                : Task.CompletedTask,
            ct);

        if (reserved)
        {
            Log.Success(Source, $"Stock reserved ({Describe(demand)})");
        }
        else
        {
            Log.Info(Source, $"Insufficient stock, nothing reserved ({Describe(demand)})");
        }

        return reserved;
    }

    public async Task ReleaseStockAsync(IReadOnlyCollection<OrderItem> items, CancellationToken ct)
    {
        Dictionary<int, int> demand = Aggregate(items);
        bool released = false;

        await Latency.WriteAsync(
            Source,
            $"stock release ({Describe(demand)})",
            async token =>
            {
                await _store.UpdateAsync<Product>(
                    DataFiles.Products,
                    products => Adjust(products, demand, 1),
                    token);
                released = true;
            },
            () => released
                ? _store.UpdateAsync<Product>(
                    DataFiles.Products,
                    products => Adjust(products, demand, -1),
                    CancellationToken.None)
                : Task.CompletedTask,
            ct);

        Log.Success(Source, $"Stock released ({Describe(demand)})");
    }

    private static Dictionary<int, int> Aggregate(IReadOnlyCollection<OrderItem> items)
    {
        return items
            .GroupBy(i => i.ProductId)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
    }

    private static string Describe(Dictionary<int, int> demand)
    {
        return string.Join(", ", demand.Select(d => $"P{d.Key} x{d.Value}"));
    }

    private static bool TryReserve(List<Product> products, Dictionary<int, int> demand)
    {
        foreach ((int productId, int quantity) in demand)
        {
            Product? product = products.Find(p => p.Id == productId);

            if (product is null || product.StockQuantity < quantity)
            {
                return false;
            }
        }

        Adjust(products, demand, -1);
        return true;
    }

    private static void Adjust(List<Product> products, Dictionary<int, int> demand, int direction)
    {
        foreach ((int productId, int quantity) in demand)
        {
            Product? product = products.Find(p => p.Id == productId);

            if (product is not null)
            {
                product.StockQuantity += direction * quantity;
            }
        }
    }
}
