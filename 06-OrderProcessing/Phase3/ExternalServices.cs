using System.Collections.Concurrent;
using OrderProcessing;

namespace OrderProcessing.Phase3;

public interface IPaymentGateway
{
    Task<PaymentResult> ProcessPaymentAsync(int orderId, decimal amount, CancellationToken ct);
    Task RefundAsync(string transactionId, CancellationToken ct);
}

public interface IEmailService
{
    Task SendConfirmationAsync(Order order, Customer customer, CancellationToken ct);
}

public sealed class PaymentGateway : IPaymentGateway
{
    private readonly PaymentRepository _payments; private readonly HashSet<int> _timeout; private readonly HashSet<int> _decline;
    public PaymentGateway(PaymentRepository payments, IEnumerable<int>? timeoutOrderIds = null, IEnumerable<int>? declineOrderIds = null)
    { _payments=payments; _timeout=(timeoutOrderIds ?? Array.Empty<int>()).ToHashSet(); _decline=(declineOrderIds ?? Array.Empty<int>()).ToHashSet(); }
    public async Task<PaymentResult> ProcessPaymentAsync(int orderId, decimal amount, CancellationToken ct)
    {
        await Latency.StepAsync(nameof(PaymentGateway), $"Authorizing order #{orderId}...", 800, ct);
        await Latency.StepAsync(nameof(PaymentGateway), "Running fraud check...", 500, ct);
        if (_decline.Contains(orderId))
        {
            var declined = new PaymentResult { Success=false, Status=PaymentStatus.Declined, Message="Card declined by issuer" };
            await _payments.AppendGatewayRecordAsync(new PaymentTransaction { OrderId=orderId, Amount=amount, Status=PaymentStatus.Declined, CreatedAt=DateTime.Now }, CancellationToken.None);
            return declined;
        }
        await Latency.StepAsync(nameof(PaymentGateway), "Capturing payment...", _timeout.Contains(orderId) ? 5000 : 700, ct);
        string txn = "TXN-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        await _payments.AppendGatewayRecordAsync(new PaymentTransaction { OrderId=orderId, Amount=amount, Status=PaymentStatus.Captured, TransactionId=txn, CreatedAt=DateTime.Now }, CancellationToken.None);
        return new PaymentResult { Success=true, Status=PaymentStatus.Captured, TransactionId=txn };
    }
    public async Task RefundAsync(string transactionId, CancellationToken ct)
    {
        await Latency.ExternalApiAsync(nameof(PaymentGateway), $"refund {transactionId}", ct);
        await _payments.MarkRefundedAsync(transactionId, CancellationToken.None);
        Log.Success(nameof(PaymentGateway), $"Transaction {transactionId} refunded");
    }
}

public sealed class EmailService : IEmailService
{
    private readonly JsonFileStore _store; private readonly Dictionary<int,int> _failAttempts; private readonly ConcurrentDictionary<int,int> _attempts = new();
    public EmailService(IReadOnlyDictionary<int,int>? failAttempts = null) : this(new JsonFileStore(), failAttempts) { }
    public EmailService(JsonFileStore store, IReadOnlyDictionary<int,int>? failAttempts = null)
    { _store=store; _failAttempts=failAttempts?.ToDictionary(x=>x.Key,x=>x.Value) ?? new(); }
    public async Task SendConfirmationAsync(Order order, Customer customer, CancellationToken ct)
    {
        int attempt = _attempts.AddOrUpdate(order.Id, 1, (_, old) => old+1);
        await Latency.SmtpAsync(nameof(EmailService), $"order #{order.Id}, attempt {attempt}", ct);
        if (_failAttempts.TryGetValue(order.Id, out int failCount) && attempt <= failCount) throw new SmtpUnavailableException($"SMTP unavailable for order #{order.Id}, attempt {attempt}");
        var message = new EmailMessage { Id=Guid.NewGuid(), OrderId=order.Id, To=customer.Email, Subject=$"Order #{order.Id} confirmation", Body=$"Your order total is {order.TotalAmount:F2}.", SentAt=DateTime.Now, Attempts=attempt };
        await _store.AppendAsync(DataFiles.Emails, message, ct);
        Log.Success(nameof(EmailService), $"Confirmation email for order #{order.Id} sent");
    }
}
