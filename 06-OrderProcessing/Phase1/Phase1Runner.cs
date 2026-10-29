using System.Diagnostics;
using System.Collections.Concurrent;
using OrderProcessing;

namespace OrderProcessing.Phase1;

public sealed class Phase1Runner(JsonFileStore store)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await DataSeeder.ResetAsync(store, ct);
        var incoming = await new IncomingOrderRepository(store).GetAllAsync(ct);
        var sync = new SyncOrderValidator(new(store), new(store), new(store));
        var sw = Stopwatch.StartNew();
        var syncResults = new List<Order>(); int syncThreadId = Environment.CurrentManagedThreadId;
        foreach (var order in incoming)
        {
            ct.ThrowIfCancellationRequested(); syncResults.Add(sync.Validate(order));
            Log.Info("SyncComparison", "Opening connection..."); Thread.Sleep(200);
            Log.Info("SyncComparison", "Beginning transaction..."); Thread.Sleep(100);
            Log.Info("SyncComparison", "Simulating write without saving results..."); Thread.Sleep(400);
            Log.Info("SyncComparison", "Simulating commit..."); Thread.Sleep(200);
        }
        sw.Stop(); var syncMs = sw.Elapsed.TotalSeconds;
        Log.Plain($"Sync Time: {syncMs:F1} seconds; distinct threads=1 (thread {syncThreadId}); ThreadPool.ThreadCount={ThreadPool.ThreadCount}");

        await DataSeeder.ResetAsync(store, ct);
        incoming = await new IncomingOrderRepository(store).GetAllAsync(ct);
        await store.WriteAllAsync(DataFiles.Orders, incoming, CancellationToken.None);
        var orders = new OrderRepository(store);
        var validator = new OrderValidator(new(store), new(store), new(store));
        sw.Restart(); var asyncThreads = new ConcurrentDictionary<int, byte>();
        foreach (var order in incoming) { ct.ThrowIfCancellationRequested(); asyncThreads.TryAdd(Environment.CurrentManagedThreadId, 0); await validator.ValidateOrderAsync(order, ct); asyncThreads.TryAdd(Environment.CurrentManagedThreadId, 0); await orders.SaveAsync(order, ct); asyncThreads.TryAdd(Environment.CurrentManagedThreadId, 0); }
        sw.Stop(); var asyncMs = sw.Elapsed.TotalSeconds;
        var validated = await orders.GetByStatusAsync(OrderStatus.Validated, ct);
        var rejected = await orders.GetByStatusAsync(OrderStatus.Rejected, ct);
        Log.Plain("===== Validation Summary =====");
        Log.Plain($"Total Orders: {validated.Count + rejected.Count}"); Log.Plain($"Validated: {validated.Count}"); Log.Plain($"Rejected: {rejected.Count}");
        foreach (var order in rejected.OrderBy(o => o.Id)) Log.Plain($" - Order #{order.Id}: {order.FailureReason}");
        Log.Plain($"Sync Time: {syncMs:F1} seconds"); Log.Plain($"Async Time: {asyncMs:F1} seconds; distinct observed threads={asyncThreads.Count}; ThreadPool.ThreadCount={ThreadPool.ThreadCount}");
        Log.Plain($"orders.json now contains {await store.CountRecordsAsync(DataFiles.Orders, ct)} records.");
        await SampleOutputExporter.SaveAsync(store, "phase1", ct);
    }
}
