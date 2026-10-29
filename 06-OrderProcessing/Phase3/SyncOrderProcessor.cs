using System.Diagnostics;
using OrderProcessing;

namespace OrderProcessing.Phase3;

public sealed class SyncOrderProcessor(JsonFileStore store)
{
    public void Process(Order order)
    {
        Log.Info(nameof(SyncOrderProcessor), "Opening connection..."); Thread.Sleep(200); Log.Info(nameof(SyncOrderProcessor), "Executing customer query..."); Thread.Sleep(500);
        var customer=store.ReadAll<Customer>(DataFiles.Customers).FirstOrDefault(c=>c.Id==order.CustomerId);
        if(customer is null){order.Status=OrderStatus.Rejected;order.FailureReason=$"Customer #{order.CustomerId} not found";return;}
        if(!customer.IsActive){order.Status=OrderStatus.Rejected;order.FailureReason="Customer is inactive";return;}
        var products=store.ReadAll<Product>(DataFiles.Products);
        foreach(var item in order.Items){var p=products.FirstOrDefault(x=>x.Id==item.ProductId);if(p is null||p.StockQuantity<item.Quantity){order.Status=OrderStatus.Rejected;order.FailureReason=$"Insufficient stock for '{p?.Name ?? $"Product #{item.ProductId}"}'";return;}}
        if(order.CouponCode is not null){Log.Info(nameof(SyncOrderProcessor), "Opening coupon connection...");Thread.Sleep(200);Log.Info(nameof(SyncOrderProcessor), "Executing coupon query...");Thread.Sleep(500);var coupon=store.ReadAll<Coupon>(DataFiles.Coupons).FirstOrDefault(c=>string.Equals(c.Code,order.CouponCode,StringComparison.OrdinalIgnoreCase));if(coupon is null||coupon.ExpiryDate.Date<DateTime.Today){order.Status=OrderStatus.Rejected;order.FailureReason=coupon is null?$"Coupon '{order.CouponCode}' does not exist":$"Coupon '{order.CouponCode}' has expired";return;}}
        order.Status=OrderStatus.Completed;order.FailureReason=null;
    }
}
