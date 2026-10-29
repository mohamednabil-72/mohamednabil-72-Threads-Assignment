using System.Diagnostics;
using System.Collections.Concurrent;
using OrderProcessing;
using OrderProcessing.Phase1;

namespace OrderProcessing.Phase2;

public sealed class Phase2Runner(JsonFileStore store)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await DataSeeder.ResetAsync(store, ct);
        BlockingWaitDemo.Run();
        var productRepo = new ProductRepository(store);
        await new RaceConditionDemo(store, productRepo).RunAsync(ct);
        await DataSeeder.ResetAsync(store, ct);
        var incoming = await new IncomingOrderRepository(store).GetAllAsync(ct);
        await store.WriteAllAsync(DataFiles.Orders, incoming, CancellationToken.None);
        var orderRepo = new OrderRepository(store);
        var validator = new OrderValidator(new(store), productRepo, new(store));
        var validationTasks = incoming.Select(async o => { await validator.ValidateOrderAsync(o, ct); await orderRepo.SaveAsync(o, ct); return o; }).ToArray();
        await Task.WhenAll(validationTasks);
        var pricing = new PricingService(store, new[] { 7 }); var pricer = new OrderPricer(pricing);
        var first = incoming.First(o => o.Id == 1);
        var pricingThreads = new ConcurrentDictionary<int, byte>(); var sw = Stopwatch.StartNew(); pricingThreads.TryAdd(Environment.CurrentManagedThreadId, 0); await pricer.PriceOrderSequentialAsync(Clone(first), ct); pricingThreads.TryAdd(Environment.CurrentManagedThreadId, 0); sw.Stop(); Log.Plain($"Order #1 sequential pricing: {sw.Elapsed.TotalSeconds:F2}s; observed threads={pricingThreads.Count}; ThreadPool.ThreadCount={ThreadPool.ThreadCount}");
        pricingThreads.Clear(); sw.Restart(); pricingThreads.TryAdd(Environment.CurrentManagedThreadId, 0); await pricer.PriceOrderAsync(Clone(first), ct); pricingThreads.TryAdd(Environment.CurrentManagedThreadId, 0); sw.Stop(); Log.Plain($"Order #1 concurrent pricing: {sw.Elapsed.TotalSeconds:F2}s; observed threads={pricingThreads.Count}; ThreadPool.ThreadCount={ThreadPool.ThreadCount}");
        var processor = new OrderProcessorV2(productRepo, orderRepo, pricer, new InvoiceGenerator(store));
        var validated = await orderRepo.GetByStatusAsync(OrderStatus.Validated, ct);
        RunSummary.Reset();
        var processingThreads = new ConcurrentDictionary<int, byte>();
        var tasks = validated.Select(async o =>
        {
            processingThreads.TryAdd(Environment.CurrentManagedThreadId, 0);
            try { var result = await processor.ProcessValidatedOrderAsync(o, ct); new RunSummary().OrderFinished(); return result; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { new RunSummary().OrderCancelled(); throw; }
            catch (Exception) when (ct.IsCancellationRequested) { new RunSummary().OrderCancelled(); throw new OperationCanceledException(ct); }
            catch { new RunSummary().OrderFinished(); throw; }
            finally { processingThreads.TryAdd(Environment.CurrentManagedThreadId, 0); }
        }).ToList();
        var processingWatch = Stopwatch.StartNew();
        if (tasks.Count > 0) Log.Plain($"First task immediately: {tasks[0].Status}");
        await Task.Delay(1000, ct);
        for (int i = 0; i < tasks.Count; i++) Log.Plain($"Task[{i}] while running: {tasks[i].Status}");
        bool cancelled = false; int taskIndex = 0;
        foreach (var task in tasks)
        {
            try { await task; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { cancelled = true; }
            catch (Exception ex) when (!ct.IsCancellationRequested) { var o = validated[taskIndex]; o.Status = OrderStatus.Failed; o.FailureReason = ex.Message; await productRepo.ReleaseStockAsync(o.Items, CancellationToken.None); await orderRepo.SaveAsync(o, CancellationToken.None); Log.Error("Phase2", $"Task[{taskIndex}] failed: {ex.Message}"); }
            Log.Plain($"Task[{taskIndex}] final: {task.Status}"); taskIndex++;
        }
        processingWatch.Stop(); Log.Plain($"Concurrent processing time: {processingWatch.Elapsed.TotalSeconds:F2}s; observed threads={processingThreads.Count}; ThreadPool.ThreadCount={ThreadPool.ThreadCount}");
        if (cancelled) throw new OperationCanceledException(ct);
        Log.Plain($"orders.json records: {await store.CountRecordsAsync(DataFiles.Orders, ct)}");
        var reportTask = new ReportGenerator(store, orderRepo, productRepo).GenerateAsync(ct);
        Log.Plain("Generating report..."); var report = await reportTask; Log.Plain(System.Text.Json.JsonSerializer.Serialize(report, JsonFileStore.Options));
        await SampleOutputExporter.SaveAsync(store, "phase2", ct);
    }
    private static Order Clone(Order o) => new() { Id=o.Id, CustomerId=o.CustomerId, Items=o.Items.Select(i=>new OrderItem{ProductId=i.ProductId,Quantity=i.Quantity,UnitPrice=i.UnitPrice}).ToList(), CouponCode=o.CouponCode, CreatedAt=o.CreatedAt, Status=o.Status };
}
