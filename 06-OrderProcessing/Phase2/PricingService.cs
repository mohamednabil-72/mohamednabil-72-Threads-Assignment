using OrderProcessing;

namespace OrderProcessing.Phase2;

public sealed class PricingService
{
    private readonly JsonFileStore _store; private readonly CustomerRepository _customers; private readonly CouponRepository _coupons; private readonly HashSet<int> _failShipping;
    public PricingService(JsonFileStore store, IEnumerable<int> failShippingForOrderIds)
    { _store = store; _customers = new(store); _coupons = new(store); _failShipping = failShippingForOrderIds.ToHashSet(); }

    public async Task<decimal> CalculateSubTotalAsync(Order order, CancellationToken ct)
    {
        await Latency.QueryAsync(nameof(PricingService), $"prices for order #{order.Id}", ct);
        var products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct);
        decimal subtotal = 0;
        foreach (var item in order.Items)
        {
            var product = products.FirstOrDefault(p => p.Id == item.ProductId) ?? throw new InvalidOperationException($"Product #{item.ProductId} not found");
            item.UnitPrice = product.Price; subtotal += item.UnitPrice * item.Quantity;
        }
        order.SubTotal = Money(subtotal); return order.SubTotal;
    }

    public async Task<decimal> GetDiscountAsync(Order order, CancellationToken ct)
    {
        await Latency.ExternalApiAsync(nameof(PricingService), $"discount for order #{order.Id}", ct);
        var customer = await _customers.GetByIdAsync(order.CustomerId, ct);
        if (customer is null) throw new CustomerNotFoundException($"Customer #{order.CustomerId} not found");
        decimal loyalty = customer.LoyaltyTier switch { LoyaltyTier.Silver => 5m, LoyaltyTier.Gold => 10m, _ => 0m };
        decimal coupon = 0;
        if (order.CouponCode is not null)
        {
            var found = await _coupons.GetByCodeAsync(order.CouponCode, ct);
            if (found is null) throw new InvalidCouponException($"Coupon '{order.CouponCode}' does not exist");
            if (found.ExpiryDate.Date < DateTime.Today) throw new InvalidCouponException($"Coupon '{order.CouponCode}' has expired");
            coupon = found.DiscountPercent;
        }
        var percent = Math.Min(30m, loyalty + coupon);
        order.Discount = Money(order.SubTotal * percent / 100m); return order.Discount;
    }

    public async Task<decimal> CalculateTotalAsync(Order order, CancellationToken ct)
    {
        await CalculateSubTotalAsync(order, ct);
        var discountTask = GetDiscountAsync(order, ct);
        var shippingTask = CalculateShippingCostAsync(order, ct);
        await Task.WhenAll(discountTask, shippingTask);
        order.TotalAmount = Money(order.SubTotal - order.Discount + order.ShippingCost);
        return order.TotalAmount;
    }

    public async Task<decimal> CalculateShippingCostAsync(Order order, CancellationToken ct)
    {
        await Latency.StepAsync(nameof(PricingService), $"Shipping handshake for order #{order.Id}...", 150, ct);
        if (_failShipping.Contains(order.Id)) throw new ShippingProviderUnavailableException("Shipping provider unavailable");
        await Latency.StepAsync(nameof(PricingService), $"Requesting shipping quote for order #{order.Id}...", 300, ct);
        await Latency.StepAsync(nameof(PricingService), "Waiting for shipping quote...", Latency.Between(500, 1500), ct);
        if (order.SubTotal >= 1000m) { order.ShippingCost = 0m; return 0m; }
        var products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct);
        decimal weight = order.Items.Sum(i => (products.FirstOrDefault(p => p.Id == i.ProductId)?.WeightKg ?? 0m) * i.Quantity);
        int quantity = order.Items.Sum(i => i.Quantity);
        order.ShippingCost = Money(5m + 1.50m * quantity + 2m * weight); return order.ShippingCost;
    }
    public static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed class OrderPricer(PricingService service)
{
    public async Task PriceOrderAsync(Order order, CancellationToken ct)
    {
        await service.CalculateSubTotalAsync(order, ct);
        var discountTask = service.GetDiscountAsync(order, ct);
        var shippingTask = service.CalculateShippingCostAsync(order, ct);
        await Task.WhenAll(discountTask, shippingTask);
        order.TotalAmount = PricingService.Money(order.SubTotal - order.Discount + order.ShippingCost);
    }
    public async Task PriceOrderSequentialAsync(Order order, CancellationToken ct)
    {
        await service.CalculateSubTotalAsync(order, ct);
        order.Discount = await service.GetDiscountAsync(order, ct);
        order.ShippingCost = await service.CalculateShippingCostAsync(order, ct);
        order.TotalAmount = PricingService.Money(order.SubTotal - order.Discount + order.ShippingCost);
    }
}
