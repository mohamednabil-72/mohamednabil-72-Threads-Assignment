using System.Diagnostics;
using System.Collections.Concurrent;
using OrderProcessing;

namespace OrderProcessing.Phase4;

public sealed record ShippingQuote(string Carrier,decimal Price,long ElapsedMs);

public interface IShippingCarrier { string Name { get; } Task<decimal> GetQuoteAsync(Order order,CancellationToken ct); }

public sealed class FastShip(IEnumerable<int>? failForOrderIds=null, IReadOnlyDictionary<int,decimal>? weights=null):IShippingCarrier
{
    private readonly HashSet<int> _fail=(failForOrderIds??Array.Empty<int>()).ToHashSet(); private readonly IReadOnlyDictionary<int,decimal> _weights=weights??new Dictionary<int,decimal>(); public string Name=>"FastShip";
    public async Task<decimal> GetQuoteAsync(Order order,CancellationToken ct){await Latency.StepAsync(Name,"Handshake",150,ct);if(_fail.Contains(order.Id))throw new ShippingProviderUnavailableException($"{Name} failed");await Latency.StepAsync(Name,"Quote request",Latency.Between(500,1500),ct);return Base(order,_weights);}
    internal static decimal Base(Order order,IReadOnlyDictionary<int,decimal> weights)=>order.SubTotal>=1000m?0m:5m+1.5m*order.Items.Sum(i=>i.Quantity)+2m*order.Items.Sum(i=>i.Quantity*(weights.TryGetValue(i.ProductId,out var w)?w:0m));
}
public sealed class CairoExpress(IEnumerable<int>? failForOrderIds=null, IReadOnlyDictionary<int,decimal>? weights=null):IShippingCarrier
{
    private readonly HashSet<int> _fail=(failForOrderIds??Array.Empty<int>()).ToHashSet(); private readonly IReadOnlyDictionary<int,decimal> _weights=weights??new Dictionary<int,decimal>();public string Name=>"CairoExpress";
    public async Task<decimal> GetQuoteAsync(Order order,CancellationToken ct){await Latency.StepAsync(Name,"Handshake",150,ct);if(_fail.Contains(order.Id)||order.Id%2==0)throw new ShippingProviderUnavailableException($"{Name} failed");await Latency.StepAsync(Name,"Quote request",Latency.Between(300,2000),ct);return FastShip.Base(order,_weights)*.95m;}
}
public sealed class NileCourier(IEnumerable<int>? failForOrderIds=null, IReadOnlyDictionary<int,decimal>? weights=null):IShippingCarrier
{
    private readonly HashSet<int> _fail=(failForOrderIds??Array.Empty<int>()).ToHashSet(); private readonly IReadOnlyDictionary<int,decimal> _weights=weights??new Dictionary<int,decimal>();public string Name=>"NileCourier";
    public async Task<decimal> GetQuoteAsync(Order order,CancellationToken ct){await Latency.StepAsync(Name,"Handshake",150,ct);if(_fail.Contains(order.Id))throw new ShippingProviderUnavailableException($"{Name} failed");await Latency.StepAsync(Name,"Quote request",Latency.Between(800,1200),ct);return FastShip.Base(order,_weights)*1.10m;}
}

public sealed class ShippingQuoteService(IEnumerable<IShippingCarrier> carriers)
{
    private readonly ConcurrentDictionary<int,byte> _threads=new();
    public int DistinctThreadCount => _threads.Count;
    public async Task<ShippingQuote> GetFastestQuoteAsync(Order order,CancellationToken ct)
    {
        var sw=Stopwatch.StartNew();using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct);budget.CancelAfter(1200);
        var active=carriers.Select(c=>Run(c,order,budget.Token,sw)).ToList();var failures=0;
        while(active.Count>0)
        {
            Task<(IShippingCarrier Carrier,decimal? Price,Exception? Error)> done;
            try{done=await Task.WhenAny(active).WaitAsync(budget.Token);}catch(OperationCanceledException) when(!ct.IsCancellationRequested){break;}
            active.Remove(done);var result=await done;
            if(result.Error is null && result.Price.HasValue){long elapsed=sw.ElapsedMilliseconds;budget.Cancel();try{await Task.WhenAll(active);}catch{}return new ShippingQuote(result.Carrier.Name,Math.Round(result.Price.Value,2,MidpointRounding.AwayFromZero),elapsed);}
            if(result.Error is not OperationCanceledException){failures++;Log.Error(result.Carrier.Name,result.Error?.Message??"carrier failed");}
        }
        budget.Cancel();if(ct.IsCancellationRequested)ct.ThrowIfCancellationRequested();
        if(failures==carriers.Count())throw new ShippingProviderUnavailableException("All carriers failed");
        return new ShippingQuote("Flat rate",25.00m,sw.ElapsedMilliseconds);
    }
    private async Task<(IShippingCarrier,decimal?,Exception?)> Run(IShippingCarrier c,Order o,CancellationToken ct,Stopwatch sw)
    {_threads.TryAdd(Environment.CurrentManagedThreadId,0);try{var price=await c.GetQuoteAsync(o,ct);_threads.TryAdd(Environment.CurrentManagedThreadId,0);return(c,price,null);}catch(OperationCanceledException){Log.Info(nameof(ShippingQuoteService),$"[{c.Name}] cancelled");return(c,null,new OperationCanceledException());}catch(Exception ex){return(c,null,ex);}}
}
