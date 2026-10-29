using System.Diagnostics;
using OrderProcessing;
using OrderProcessing.Phase2;

namespace OrderProcessing.Phase3;

public sealed class Phase3Runner(JsonFileStore store)
{
    public async Task RunAsync(CancellationToken ct)
    {
        RunSummary.Reset();
        // Sync comparison: ten dedicated threads, no injected failures.
        await DataSeeder.ResetAsync(store,ct);
        var ordersIn=await new IncomingOrderRepository(store).GetAllAsync(ct);
        var syncProcessor=new SyncOrderProcessor(store); var syncOrders=ordersIn.Where(o=>o.Id<=10).ToList(); var syncThreads=new List<Thread>(); var sw=Stopwatch.StartNew();
        foreach(var order in syncOrders){var captured=order;var t=new Thread(()=>syncProcessor.Process(captured));syncThreads.Add(t);t.Start();}
        foreach(var t in syncThreads)t.Join(); sw.Stop(); Log.Plain($"Sync 10-thread comparison: {sw.Elapsed.TotalSeconds:F2}s, distinct threads={syncThreads.Select(t=>t.ManagedThreadId).Distinct().Count()}, ThreadPool.ThreadCount={ThreadPool.ThreadCount}; Completed={syncOrders.Count(o=>o.Status==OrderStatus.Completed)}, Rejected={syncOrders.Count(o=>o.Status==OrderStatus.Rejected)}");
        await DataSeeder.ResetAsync(store,ct);
        var customers=new CustomerRepository(store);var products=new ProductRepository(store);var orders=new OrderRepository(store);var pricing=new PricingService(store,Array.Empty<int>());
        var processor=new OrderProcessor(new CustomerService(customers),products,new InventoryService(products),orders,new CheckoutPricingService(pricing),new PaymentGateway(new PaymentRepository(store)),new EmailService(store),store);
        var asyncOrders=await new IncomingOrderRepository(store).GetAllAsync(ct);var asyncCompareOrders=asyncOrders.Where(o=>o.Id<=10).ToList();var asyncThreads=new System.Collections.Concurrent.ConcurrentDictionary<int,byte>();var tasks=asyncCompareOrders.Select(async o=>{asyncThreads.TryAdd(Environment.CurrentManagedThreadId,0);var result=await processor.ProcessOrderAsync(o,ct);asyncThreads.TryAdd(Environment.CurrentManagedThreadId,0);return result;}).ToArray(); sw.Restart(); await Task.WhenAll(tasks);sw.Stop();Log.Plain($"Async 10-task comparison: {sw.Elapsed.TotalSeconds:F2}s, distinct observed threads={asyncThreads.Count}, ThreadPool.ThreadCount={ThreadPool.ThreadCount}; Completed={asyncCompareOrders.Count(o=>o.Status==OrderStatus.Completed)}, Rejected={asyncCompareOrders.Count(o=>o.Status==OrderStatus.Rejected)}");
        // Main checkout with the injected failures from the specification.
        await DataSeeder.ResetAsync(store,ct);
        customers=new CustomerRepository(store);products=new ProductRepository(store);orders=new OrderRepository(store,new[]{13});pricing=new PricingService(store,Array.Empty<int>());
        var payment=new PaymentGateway(new PaymentRepository(store),new[]{5},new[]{8});
        var email=new EmailService(store,new Dictionary<int,int>{{3,2},{17,int.MaxValue}});
        processor=new OrderProcessor(new CustomerService(customers),products,new InventoryService(products),orders,new CheckoutPricingService(pricing),payment,email,store);
        asyncOrders=await new IncomingOrderRepository(store).GetAllAsync(ct);
        await store.WriteAllAsync(DataFiles.Orders, asyncOrders, CancellationToken.None);
        RunSummary.Reset();
        var runTasks=asyncOrders.Select(ProcessTrackedAsync).ToList(); bool cancelled=false;
        foreach(var task in runTasks){try{await task;}catch(OperationCanceledException) when(ct.IsCancellationRequested){cancelled=true;}catch(Exception ex){Log.Error("Phase3",ex.Message);}}
        if(cancelled) throw new OperationCanceledException(ct);
        var final=await orders.GetAllAsync(ct);

        async Task<bool> ProcessTrackedAsync(Order order)
        {
            try { var result=await processor.ProcessOrderAsync(order,ct); new RunSummary().OrderFinished(); return result; }
            catch(OperationCanceledException) when(ct.IsCancellationRequested) { new RunSummary().OrderCancelled(); throw; }
        }
        Log.Plain("Order | Status | Total | Reason");foreach(var o in final.OrderBy(o=>o.Id))Log.Plain($"{o.Id,2} | {o.Status,-12} | {o.TotalAmount,8:F2} | {o.FailureReason}");
        Log.Plain($"Final orders: {final.Count}; ThreadPool.ThreadCount={ThreadPool.ThreadCount}");
        await SampleOutputExporter.SaveAsync(store, "phase3", ct);
        await RunFireAndForgetDemoAsync(ct);
    }

    private async Task RunFireAndForgetDemoAsync(CancellationToken ct)
    {
        Log.Plain("--- Broken fire-and-forget demo ---");
        string demoDirectory=Path.Combine(DataFiles.DirectoryPath,"fire_and_forget_demo");Directory.CreateDirectory(demoDirectory);var demoStore=new JsonFileStore(demoDirectory);
        var repo=new OrderRepository(demoStore);var email=new EmailService(demoStore);var customer=(await new CustomerRepository(store).GetByIdAsync(1,ct))!;var source=(await new IncomingOrderRepository(store).GetAllAsync(ct)).First();
        var order=new Order{Id=source.Id,CustomerId=source.CustomerId,Items=source.Items.Select(i=>new OrderItem{ProductId=i.ProductId,Quantity=i.Quantity,UnitPrice=i.UnitPrice}).ToList(),CouponCode=source.CouponCode,Status=OrderStatus.Completed,TotalAmount=source.TotalAmount,CreatedAt=source.CreatedAt};
        await BrokenFireAndForgetAsync(order,customer,repo,email,ct);await Task.Delay(100,ct);
        Log.Plain("The broken method returned while its SMTP delay is still running.");
        Log.Plain("--- Fixed awaited email demo ---");await FixedAsync(order,customer,repo,email,ct);
    }
    private static async Task BrokenFireAndForgetAsync(Order order,Customer customer,OrderRepository repo,IEmailService email,CancellationToken ct)
    {
        await repo.SaveAsync(order,ct);
#pragma warning disable CS4014
        email.SendConfirmationAsync(order,customer,ct);
#pragma warning restore CS4014
        Console.WriteLine("Order processing finished");
    }
    private static async Task FixedAsync(Order order,Customer customer,OrderRepository repo,IEmailService email,CancellationToken ct)
    { await repo.SaveAsync(order,ct);await email.SendConfirmationAsync(order,customer,ct);Console.WriteLine("Order processing finished after email"); }
}
