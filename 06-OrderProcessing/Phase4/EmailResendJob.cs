using System.Collections.Concurrent;
using System.Diagnostics;
using OrderProcessing;
using OrderProcessing.Phase3;

namespace OrderProcessing.Phase4;

public sealed class EmailResendJob(IEmailService emailService)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var customer=new Customer{Id=1,Name="Test Customer",Email="test.customer@example.com",IsActive=true,LoyaltyTier=LoyaltyTier.Bronze};
        var orders=Enumerable.Range(1001,30).Select(id=>new Order{Id=id,CustomerId=customer.Id,Status=OrderStatus.Completed,TotalAmount=100m,CreatedAt=DateTime.Now}).ToArray();
        int active=0,max=0;var threads=new ConcurrentDictionary<int,byte>();var sw=Stopwatch.StartNew();
        await Parallel.ForEachAsync(orders,new ParallelOptions{CancellationToken=ct,MaxDegreeOfParallelism=4},async(order,token)=>
        {
            int now=Interlocked.Increment(ref active);int seen;do{seen=Volatile.Read(ref max);if(now<=seen)break;}while(Interlocked.CompareExchange(ref max,now,seen)!=seen);
            threads.TryAdd(Environment.CurrentManagedThreadId,0);try{await emailService.SendConfirmationAsync(order,customer,token);}finally{Interlocked.Decrement(ref active);}
        });
        sw.Stop();var all=await new JsonFileStore().ReadAllAsync<EmailMessage>(DataFiles.Emails,ct);var test=all.Where(e=>e.OrderId>=1001&&e.OrderId<=1030).ToList();
        Log.Plain($"Email resend: {test.Count}/30 saved once; max simultaneous sends={max}; time={sw.Elapsed.TotalSeconds:F2}s; distinct threads={threads.Count}; ThreadPool.ThreadCount={ThreadPool.ThreadCount}");
    }
}
