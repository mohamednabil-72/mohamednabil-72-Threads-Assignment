using OrderProcessing;

namespace OrderProcessing.Phase2;

public sealed class RaceConditionDemo(JsonFileStore store, ProductRepository products)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await store.UpdateAsync<Product>(DataFiles.Products, xs => { var p = xs.First(x => x.Id == 4); p.StockQuantity = 1; }, ct);
        Log.Plain("--- Unsafe reservation (expected oversell) ---");
        var unsafeResults = await Task.WhenAll(products.ReserveStockUnsafeAsync(4, 1, ct), products.ReserveStockUnsafeAsync(4, 1, ct));
        Log.Plain($"Unsafe results: {unsafeResults[0]}, {unsafeResults[1]}; stock read back: {(await products.GetByIdAsync(4, ct))?.StockQuantity}");
        await store.UpdateAsync<Product>(DataFiles.Products, xs => { var p = xs.First(x => x.Id == 4); p.StockQuantity = 1; }, ct);
        Log.Plain("--- Safe reservation ---");
        var safe = await Task.WhenAll(products.ReserveStockAsync(4, 1, ct), products.ReserveStockAsync(4, 1, ct));
        Log.Plain($"Safe results: {safe[0]}, {safe[1]}; stock read back: {(await products.GetByIdAsync(4, ct))?.StockQuantity}");
    }

}
