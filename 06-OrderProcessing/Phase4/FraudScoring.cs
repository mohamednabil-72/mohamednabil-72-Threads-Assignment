using System.Collections.Concurrent;
using System.Diagnostics;
using OrderProcessing;

namespace OrderProcessing.Phase4;

public sealed class FraudScoring
{
    public static int Score(int orderId)
    {
        long h=orderId;for(int i=0;i<100_000;i++)h=(h*31+i)%1_000_000_007;return (int)(h%100);
    }
    public async Task RunAsync(CancellationToken ct)
    {
        var ids=Enumerable.Range(1,10_000).ToArray();var sw=Stopwatch.StartNew();var sequential=new List<int>();foreach(int id in ids){ct.ThrowIfCancellationRequested();if(Score(id)>=90)sequential.Add(id);}sw.Stop();var seqMs=sw.Elapsed.TotalMilliseconds;
        var parallel=new ConcurrentBag<int>();var threads=new ConcurrentDictionary<int,byte>();sw.Restart();await Parallel.ForEachAsync(ids,new ParallelOptions{CancellationToken=ct,MaxDegreeOfParallelism=Environment.ProcessorCount},(id,token)=>{threads.TryAdd(Environment.CurrentManagedThreadId,0);if(Score(id)>=90)parallel.Add(id);return ValueTask.CompletedTask;});sw.Stop();var parMs=sw.Elapsed.TotalMilliseconds;
        var a=sequential.Order().ToArray();var b=parallel.Order().ToArray();Log.Plain($"Fraud scoring sequential: {a.Length} flagged in {seqMs:F0} ms");Log.Plain($"Fraud scoring parallel: {b.Length} flagged in {parMs:F0} ms; speed-up={(parMs==0?0:seqMs/parMs):F2}x; distinct threads={threads.Count}; ProcessorCount={Environment.ProcessorCount}; ThreadPool.ThreadCount={ThreadPool.ThreadCount}");Log.Plain($"Same results: {a.SequenceEqual(b)}; first ids: {string.Join(", ",b.Take(5))}");
    }
}
