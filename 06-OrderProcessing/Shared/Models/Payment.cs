namespace OrderProcessing.Shared.Models;

public class PaymentTransaction
{
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public string? TransactionId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PaymentResult
{
    public bool Success { get; set; }
    public string? TransactionId { get; set; }
    public PaymentStatus Status { get; set; }
    public string? Message { get; set; }
}
