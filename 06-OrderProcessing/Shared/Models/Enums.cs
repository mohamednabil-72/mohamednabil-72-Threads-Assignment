namespace OrderProcessing.Shared.Models;

public enum LoyaltyTier
{
    Bronze,
    Silver,
    Gold
}

public enum OrderStatus
{
    Pending,
    Validated,
    Rejected,
    PaymentFailed,
    Completed,
    Failed
}

public enum PaymentStatus
{
    Captured,
    Declined,
    Refunded
}
