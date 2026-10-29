namespace OrderProcessing.Shared.Repositories;

public sealed class CouponRepository
{
    private const string Source = nameof(CouponRepository);

    private readonly JsonFileStore _store;

    public CouponRepository(JsonFileStore store)
    {
        _store = store;
    }

    public async Task<Coupon?> GetByCodeAsync(string code, CancellationToken ct)
    {
        await Latency.QueryAsync(Source, $"Coupon {code}", ct);

        List<Coupon> coupons = await _store.ReadAllAsync<Coupon>(DataFiles.Coupons, ct);
        Coupon? coupon = coupons.Find(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

        Log.Info(Source, coupon is null
            ? $"Coupon {code} not found"
            : $"Coupon {coupon.Code} found ({coupon.DiscountPercent}% until {coupon.ExpiryDate:yyyy-MM-dd})");

        return coupon;
    }
}
