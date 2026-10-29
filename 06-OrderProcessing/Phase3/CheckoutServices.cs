using OrderProcessing;
using OrderProcessing.Phase2;

namespace OrderProcessing.Phase3;

public sealed class CustomerService(CustomerRepository repository)
{
    public async Task<Customer> GetCustomerAsync(int id, CancellationToken ct)
        => await repository.GetByIdAsync(id, ct) ?? throw new CustomerNotFoundException($"Customer #{id} not found");
}

public sealed class InventoryService(ProductRepository repository)
{
    public Task<bool> ReserveStockAsync(Order order, CancellationToken ct) => repository.ReserveStockAsync(order.Items, ct);
    public Task ReleaseStockAsync(Order order, CancellationToken ct) => repository.ReleaseStockAsync(order.Items, ct);
}

public sealed class CheckoutPricingService(PricingService pricing)
{
    public async Task<decimal> CalculateTotalAsync(Order order, CancellationToken ct)
    {
        await pricing.CalculateSubTotalAsync(order, ct);
        var discount = pricing.GetDiscountAsync(order, ct);
        var shipping = pricing.CalculateShippingCostAsync(order, ct);
        await Task.WhenAll(discount, shipping);
        order.TotalAmount = PricingService.Money(order.SubTotal - order.Discount + order.ShippingCost);
        return order.TotalAmount;
    }
}
