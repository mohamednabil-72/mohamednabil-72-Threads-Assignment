namespace OrderProcessing.Shared.Repositories;

public sealed class IncomingOrderRepository
{
    private const string Source = nameof(IncomingOrderRepository);

    private readonly JsonFileStore _store;

    public IncomingOrderRepository(JsonFileStore store)
    {
        _store = store;
    }

    public async Task<List<Order>> GetAllAsync(CancellationToken ct)
    {
        await Latency.QueryAsync(Source, "all incoming orders", ct);

        List<Order> orders = await _store.ReadAllAsync<Order>(DataFiles.IncomingOrders, ct);

        Log.Info(Source, $"{orders.Count} incoming orders loaded");

        return orders;
    }
}
