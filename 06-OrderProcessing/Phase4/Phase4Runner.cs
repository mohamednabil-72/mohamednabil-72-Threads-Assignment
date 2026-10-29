using System.Diagnostics;
using OrderProcessing;
using OrderProcessing.Phase2;
using OrderProcessing.Phase3;

namespace OrderProcessing.Phase4;

public sealed class Phase4Runner(JsonFileStore store)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await RunLiveCheckoutAsync(ct);
        await RunDashboardAsync(ct);
        await RunShippingAsync(ct);
        await new FraudScoring().RunAsync(ct);
        await new EmailResendJob(new EmailService(store)).RunAsync(ct);
        await SampleOutputExporter.SaveAsync(store, "phase4", ct);
    }

    private async Task RunLiveCheckoutAsync(CancellationToken ct)
    {
        Log.Plain("===== Phase 4.1 Live checkout progress =====");RunSummary.Reset();await DataSeeder.ResetAsync(store,ct);
        var customers=new CustomerRepository(store);var products=new ProductRepository(store);var orders=new OrderRepository(store,new[]{13});
        var processor=new OrderProcessor(new CustomerService(customers),products,new InventoryService(products),orders,new CheckoutPricingService(new PricingService(store,Array.Empty<int>())),new PaymentGateway(new PaymentRepository(store),new[]{5},new[]{8}),new EmailService(store,new Dictionary<int,int>{{3,2},{17,int.MaxValue}}),store);
        var incoming=await new IncomingOrderRepository(store).GetAllAsync(ct);await store.WriteAllAsync(DataFiles.Orders,incoming,CancellationToken.None);int done=0;var total=Stopwatch.StartNew();var tasks=new List<Task>();var summary=new RunSummary();
        foreach(var order in incoming)
        {
            tasks.Add(ProcessAndReportAsync(order));
        }
        await Task.WhenAll(tasks);total.Stop();
        var final=await orders.GetAllAsync(ct);Log.Plain("Final status counts:");foreach(var group in final.GroupBy(o=>o.Status).OrderBy(g=>g.Key))Log.Plain($"{group.Key}: {group.Count()}");
        Log.Plain($"Live checkout total: {total.Elapsed.TotalSeconds:F2}s; orders={final.Count}");

        async Task ProcessAndReportAsync(Order order)
        {
            var sw=Stopwatch.StartNew();
            try { await processor.ProcessOrderAsync(order,ct); summary.OrderFinished(); }
            catch(OperationCanceledException) when(ct.IsCancellationRequested) { summary.OrderCancelled(); throw; }
            finally
            {
                sw.Stop();int n=Interlocked.Increment(ref done);
                Log.Plain($"[{n,2}/20] Order #{order.Id} -> {order.Status} ({order.FailureReason ?? "OK"}) after {sw.Elapsed.TotalSeconds:F1} s");
            }
        }
    }

    private async Task RunDashboardAsync(CancellationToken ct)
    {
        Log.Plain("===== Phase 4.2 Customer dashboard =====");var service=new CustomerDashboardService(store,new[]{99});var sw=Stopwatch.StartNew();var dashboard=await service.LoadAsync(3,ct);sw.Stop();
        Log.Plain($"Customer #3 {dashboard.Customer?.Name} — {dashboard.Orders.Count} orders ({string.Join(", ",dashboard.Orders.Select(o=>$"#{o.Id} {o.Status}"))}), {dashboard.Payments.Count} payments, {dashboard.Emails.Count} emails; load={sw.Elapsed.TotalSeconds:F2}s; observed threads={service.DistinctThreadCount}; ThreadPool.ThreadCount={ThreadPool.ThreadCount}");
        try{await service.LoadAsync(99,ct);}catch(AggregateException ex){foreach(var inner in ex.Flatten().InnerExceptions)Log.Error("Dashboard",inner.Message);}
    }

    private async Task RunShippingAsync(CancellationToken ct)
    {
        Log.Plain("===== Phase 4.3 Fastest shipping quote =====");var orders=await new OrderRepository(store).GetAllAsync(ct);var products=await store.ReadAllAsync<Product>(DataFiles.Products,ct);var weights=products.ToDictionary(p=>p.Id,p=>p.WeightKg);var selected=orders.OrderBy(o=>o.Id).Take(5).ToList();
        foreach(var order in selected)
        {
            var carriers=new IShippingCarrier[]{new FastShip(weights:weights),new CairoExpress(weights:weights),new NileCourier(weights:weights)};var sw=Stopwatch.StartNew();var quoteService=new ShippingQuoteService(carriers);
            try{var quote=await quoteService.GetFastestQuoteAsync(order,ct);sw.Stop();Log.Plain($"Order #{order.Id}: {quote.Carrier}, {quote.Price:F2}, {quote.ElapsedMs} ms; observed threads={quoteService.DistinctThreadCount}; ThreadPool.ThreadCount={ThreadPool.ThreadCount}");}
            catch(ShippingProviderUnavailableException ex){Log.Error("Shipping",ex.Message);}
        }
        try{var allFail=new IShippingCarrier[]{new FastShip(new[]{6},weights),new CairoExpress(new[]{6},weights),new NileCourier(new[]{6},weights)};await new ShippingQuoteService(allFail).GetFastestQuoteAsync(new Order{Id=6,SubTotal=50},ct);}catch(ShippingProviderUnavailableException ex){Log.Error("Shipping",$"All-failed demo: {ex.Message}");}
    }
}
