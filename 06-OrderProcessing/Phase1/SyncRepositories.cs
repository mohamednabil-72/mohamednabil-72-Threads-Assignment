using System.Diagnostics;
using OrderProcessing;

namespace OrderProcessing.Phase1;

public sealed class SyncCustomerRepository(JsonFileStore store)
{
    public Customer? GetById(int id)
    {
        Log.Info(nameof(SyncCustomerRepository), "Opening connection..."); Thread.Sleep(200); Log.Info(nameof(SyncCustomerRepository), "Executing customer query..."); Thread.Sleep(Latency.Between(300, 600));
        return store.ReadAll<Customer>(DataFiles.Customers).FirstOrDefault(x => x.Id == id);
    }
}

public sealed class SyncProductRepository(JsonFileStore store)
{
    public Product? GetById(int id)
    {
        Log.Info(nameof(SyncProductRepository), "Opening connection..."); Thread.Sleep(200); Log.Info(nameof(SyncProductRepository), "Executing product query..."); Thread.Sleep(Latency.Between(300, 600));
        return store.ReadAll<Product>(DataFiles.Products).FirstOrDefault(x => x.Id == id);
    }
}

public sealed class SyncCouponRepository(JsonFileStore store)
{
    public Coupon? GetByCode(string code)
    {
        Log.Info(nameof(SyncCouponRepository), "Opening connection..."); Thread.Sleep(200); Log.Info(nameof(SyncCouponRepository), "Executing coupon query..."); Thread.Sleep(Latency.Between(300, 600));
        return store.ReadAll<Coupon>(DataFiles.Coupons).FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class SyncOrderValidator(SyncCustomerRepository customers, SyncProductRepository products, SyncCouponRepository coupons)
{
    public Order Validate(Order order)
    {
        var customer = customers.GetById(order.CustomerId);
        if (customer is null) return Reject(order, $"Customer #{order.CustomerId} not found");
        if (!customer.IsActive) return Reject(order, "Customer is inactive");
        var productChecks = order.Items.Select(item => (Item: item, Product: products.GetById(item.ProductId))).ToList();
        var missing = productChecks.Where(x => x.Product is null).ToList();
        if (missing.Count > 0) return Reject(order, $"Product #{missing[0].Item.ProductId} not found");
        foreach (var check in productChecks)
            if (check.Product!.StockQuantity < check.Item.Quantity) return Reject(order, $"Insufficient stock for '{check.Product.Name}'");
        if (order.CouponCode is not null)
        {
            var coupon = coupons.GetByCode(order.CouponCode);
            if (coupon is null) return Reject(order, $"Coupon '{order.CouponCode}' does not exist");
            if (coupon.ExpiryDate.Date < DateTime.Today) return Reject(order, $"Coupon '{order.CouponCode}' has expired");
        }
        order.Status = OrderStatus.Validated; order.FailureReason = null; return order;
    }
    private static Order Reject(Order order, string reason) { order.Status = OrderStatus.Rejected; order.FailureReason = reason; return order; }
}
