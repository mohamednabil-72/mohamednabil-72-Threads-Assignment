using System.Text.Json;
using OrderProcessing;

namespace OrderProcessing.Phase2;

public sealed class ReportGenerator(JsonFileStore store, OrderRepository orders, ProductRepository products)
{
    public async Task<Report> GenerateAsync(CancellationToken ct)
    {
        var allTask = orders.GetAllAsync(ct);
        var productTask = products.GetByIdAsync(1, ct);
        await Task.WhenAll(allTask, productTask);
        var all = await allTask;
        var completed = all.Where(o => o.Status == OrderStatus.Completed).ToList();
        var totals = completed.SelectMany(o => o.Items).GroupBy(i => i.ProductId).Select(g => new { Id = g.Key, Qty = g.Sum(x => x.Quantity) }).OrderByDescending(x => x.Qty).ThenBy(x => x.Id).FirstOrDefault();
        var top = totals is null ? null : await products.GetByIdAsync(totals.Id, ct);
        var revenue = PricingService.Money(completed.Sum(o => o.TotalAmount));
        var report = new Report { TotalOrders = all.Count, CompletedCount = all.Count(o => o.Status == OrderStatus.Completed), RejectedCount = all.Count(o => o.Status == OrderStatus.Rejected), FailedCount = all.Count(o => o.Status == OrderStatus.Failed), PaymentFailedCount = all.Count(o => o.Status == OrderStatus.PaymentFailed), TotalRevenue = revenue, AverageOrderValue = completed.Count == 0 ? 0 : PricingService.Money(revenue / completed.Count), TopSellingProduct = top?.Name, TopSellingQuantity = totals?.Qty ?? 0, GeneratedAt = DateTime.Now };
        string dir = Path.Combine(DataFiles.DirectoryPath, DataFiles.ReportsFolder); Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"report_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(report, JsonFileStore.Options), ct);
        return report;
    }
}
