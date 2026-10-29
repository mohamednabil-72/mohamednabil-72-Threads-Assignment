using OrderProcessing;

namespace OrderProcessing.Phase2;

public sealed class OrderProcessorV2(ProductRepository products, OrderRepository orders, OrderPricer pricer, InvoiceGenerator invoice)
{
    public async Task<bool> ProcessValidatedOrderAsync(Order order, CancellationToken ct)
    {
        var reserved = new List<OrderItem>();
        try
        {
            foreach (var item in order.Items)
            {
                if (!await products.ReserveStockAsync(item.ProductId, item.Quantity, ct))
                {
                    foreach (var r in reserved) await products.ReleaseStockAsync(r.ProductId, r.Quantity, CancellationToken.None);
                    reserved.Clear();
                    var p = await products.GetByIdAsync(item.ProductId, CancellationToken.None);
                    order.Status = OrderStatus.Rejected; order.FailureReason = $"Insufficient stock for '{p?.Name ?? $"Product #{item.ProductId}"}'";
                    await orders.SaveAsync(order, CancellationToken.None); return false;
                }
                reserved.Add(item);
            }
            await pricer.PriceOrderAsync(order, ct);
            await invoice.GenerateAsync(order, ct);
            order.Status = OrderStatus.Completed; order.FailureReason = null;
            await orders.SaveAsync(order, ct); return true;
        }
        finally
        {
            if (ct.IsCancellationRequested)
            {
                foreach (var item in reserved) await products.ReleaseStockAsync(item.ProductId, item.Quantity, CancellationToken.None);
                order.Status = OrderStatus.Pending; order.FailureReason = "Cancelled";
                await orders.SaveAsync(order, CancellationToken.None);
            }
        }
    }
}
