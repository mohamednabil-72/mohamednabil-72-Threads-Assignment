namespace OrderProcessing.Shared.Repositories;

public sealed class PaymentRepository
{
    private const string Source = nameof(PaymentRepository);

    private readonly JsonFileStore _store;

    public PaymentRepository(JsonFileStore store)
    {
        _store = store;
    }



    public Task AppendGatewayRecordAsync(PaymentTransaction transaction, CancellationToken ct)
        => _store.AppendAsync(DataFiles.Payments, transaction, ct);

    public Task MarkRefundedAsync(string transactionId, CancellationToken ct)
        => _store.UpdateAsync<PaymentTransaction>(DataFiles.Payments, payments =>
        {
            var payment = payments.LastOrDefault(p => p.TransactionId == transactionId && p.Status == PaymentStatus.Captured);
            if (payment is not null) payment.Status = PaymentStatus.Refunded;
        }, ct);

    public async Task RefundAsync(string transactionId, CancellationToken ct)
    {
        await Latency.WriteAsync(Source, $"refund transaction {transactionId}", async token =>
        {
            await _store.UpdateAsync<PaymentTransaction>(DataFiles.Payments, payments =>
            {
                var payment = payments.LastOrDefault(p => p.TransactionId == transactionId && p.Status == PaymentStatus.Captured);
                if (payment is not null) payment.Status = PaymentStatus.Refunded;
            }, token);
        }, null, ct);
    }

    public async Task SaveAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        bool added = false;

        await Latency.WriteAsync(
            Source,
            $"payment of order #{transaction.OrderId} ({transaction.Status})",
            async token =>
            {
                await _store.AppendAsync(DataFiles.Payments, transaction, token);
                added = true;
            },
            () => added
                ? _store.UpdateAsync<PaymentTransaction>(
                    DataFiles.Payments,
                    payments =>
                    {
                        int index = payments.FindLastIndex(p =>
                            p.OrderId == transaction.OrderId &&
                            p.Status == transaction.Status &&
                            p.TransactionId == transaction.TransactionId);

                        if (index >= 0)
                        {
                            payments.RemoveAt(index);
                        }
                    },
                    CancellationToken.None)
                : Task.CompletedTask,
            ct);

        Log.Success(Source, $"Payment of order #{transaction.OrderId} saved ({transaction.Status})");
    }
}
