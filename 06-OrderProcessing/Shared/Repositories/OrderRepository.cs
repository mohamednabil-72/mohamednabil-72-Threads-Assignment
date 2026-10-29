namespace OrderProcessing.Shared.Repositories;

public sealed class OrderRepository
{
    private const string Source = nameof(OrderRepository);

    private readonly JsonFileStore _store;
    private readonly HashSet<int> _failSaveForOrderIds;

    public OrderRepository(JsonFileStore store, IEnumerable<int>? failSaveForOrderIds = null)
    {
        _store = store;
        _failSaveForOrderIds = (failSaveForOrderIds ?? Array.Empty<int>()).ToHashSet();
    }

    public async Task<List<Order>> GetByStatusAsync(OrderStatus status, CancellationToken ct)
    {
        await Latency.QueryAsync(Source, $"orders with status {status}", ct);
        List<Order> orders = await _store.ReadAllAsync<Order>(DataFiles.Orders, ct);
        return orders.Where(o => o.Status == status).ToList();
    }

    public async Task<List<Order>> GetAllAsync(CancellationToken ct)
    {
        await Latency.QueryAsync(Source, "all saved orders", ct);

        List<Order> orders = await _store.ReadAllAsync<Order>(DataFiles.Orders, ct);

        Log.Info(Source, $"{orders.Count} saved orders loaded");

        return orders;
    }

    public async Task SaveAsync(Order order, CancellationToken ct)
    {
        if (order.Status == OrderStatus.Completed && _failSaveForOrderIds.Contains(order.Id))
        {
            await Latency.StepAsync(Source, "Opening connection...", 200, ct);
            await Latency.StepAsync(Source, "Beginning transaction...", 100, ct);
            await Latency.StepAsync(Source, $"Writing Order #{order.Id}...", 400, ct);
            await Latency.StepAsync(Source, "Committing transaction...", 200, ct);
            throw new DatabaseWriteException("Connection lost during commit");
        }
        Order? previous = null;
        bool applied = false;

        await Latency.WriteAsync(
            Source,
            $"Order #{order.Id}",
            async token =>
            {
                previous = await _store.UpdateAsync<Order, Order?>(
                    DataFiles.Orders,
                    orders =>
                    {
                        int index = orders.FindIndex(o => o.Id == order.Id);
                        Order? replaced = index >= 0 ? orders[index] : null;

                        if (index >= 0)
                        {
                            orders[index] = order;
                        }
                        else
                        {
                            orders.Add(order);
                        }

                        return replaced;
                    },
                    token);
                applied = true;
            },
            () => applied
                ? _store.UpdateAsync<Order>(
                    DataFiles.Orders,
                    orders =>
                    {
                        int index = orders.FindIndex(o => o.Id == order.Id);

                        if (index < 0)
                        {
                            return;
                        }

                        if (previous is not null)
                        {
                            orders[index] = previous;
                        }
                        else
                        {
                            orders.RemoveAt(index);
                        }
                    },
                    CancellationToken.None)
                : Task.CompletedTask,
            ct);

        Log.Success(Source, $"Order #{order.Id} saved ({order.Status})");
    }
}
