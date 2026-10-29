using OrderProcessing;

namespace OrderProcessing.Phase1;

public sealed class OrderValidator
{
    private readonly CustomerRepository _customers;
    private readonly ProductRepository _products;
    private readonly CouponRepository _coupons;
    public OrderValidator(CustomerRepository customers, ProductRepository products, CouponRepository coupons)
        => (_customers, _products, _coupons) = (customers, products, coupons);

    public async Task<Order> ValidateOrderAsync(Order order, CancellationToken ct)
    {
        var customer = await _customers.GetByIdAsync(order.CustomerId, ct);
        if (customer is null) return await RejectAsync(order, $"Customer #{order.CustomerId} not found", ct);
        if (!customer.IsActive) return await RejectAsync(order, "Customer is inactive", ct);
        var productChecks = new List<(OrderItem Item, Product? Product)>();
        foreach (var item in order.Items) productChecks.Add((item, await _products.GetByIdAsync(item.ProductId, ct)));
        var missing = productChecks.Where(x => x.Product is null).ToList();
        if (missing.Count > 0) return await RejectAsync(order, $"Product #{missing[0].Item.ProductId} not found", ct);
        foreach (var check in productChecks)
            if (check.Product!.StockQuantity < check.Item.Quantity)
                return await RejectAsync(order, $"Insufficient stock for '{check.Product.Name}'", ct);
        if (order.CouponCode is not null)
        {
            var coupon = await _coupons.GetByCodeAsync(order.CouponCode, ct);
            if (coupon is null) return await RejectAsync(order, $"Coupon '{order.CouponCode}' does not exist", ct);
            if (coupon.ExpiryDate.Date < DateTime.Today) return await RejectAsync(order, $"Coupon '{order.CouponCode}' has expired", ct);
        }
        order.Status = OrderStatus.Validated; order.FailureReason = null;
        return order;
    }
    private Task<Order> RejectAsync(Order order, string reason, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); order.Status = OrderStatus.Rejected; order.FailureReason = reason; return Task.FromResult(order); }
}
