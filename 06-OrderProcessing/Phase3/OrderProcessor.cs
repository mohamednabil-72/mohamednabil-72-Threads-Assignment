using System.Diagnostics;
using OrderProcessing;
using OrderProcessing.Phase2;

namespace OrderProcessing.Phase3;

public sealed class OrderProcessor
{
    private readonly CustomerService _customerService; private readonly ProductRepository _products; private readonly InventoryService _inventory; private readonly OrderRepository _orders;
    private readonly CheckoutPricingService _pricing; private readonly IPaymentGateway _payment; private readonly IEmailService _email; private readonly JsonFileStore _store;
    public OrderProcessor(CustomerService customerService, ProductRepository products, InventoryService inventory, OrderRepository orders, CheckoutPricingService pricing, IPaymentGateway payment, IEmailService email, JsonFileStore store)
    { _customerService=customerService; _products=products; _inventory=inventory; _orders=orders; _pricing=pricing; _payment=payment; _email=email; _store=store; }

    public async Task<bool> ProcessOrderAsync(Order order, CancellationToken ct)
    {
        var total = Stopwatch.StartNew(); bool stockReserved = false; string? transactionId = null; Customer? customer = null;
        async Task SaveStatusAsync(OrderStatus status, string? reason)
        { order.Status=status; order.FailureReason=reason; await _orders.SaveAsync(order, CancellationToken.None); }
        try
        {
            await Step(order,1,"Customer lookup",async token => { customer=await _customerService.GetCustomerAsync(order.CustomerId,token); if(!customer.IsActive) throw new InvalidOperationException("Customer is inactive"); },ct);
            await Step(order,2,"Reserve stock",async token => { stockReserved=await _inventory.ReserveStockAsync(order,token); if(!stockReserved) throw new InvalidOperationException(await StockReasonAsync(order,token)); },ct);
            await Step(order,3,"Pricing",async token => { await _pricing.CalculateTotalAsync(order,token); },ct);
            using (var paymentCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                paymentCts.CancelAfter(TimeSpan.FromSeconds(3));
                try { await Step(order,4,"Payment",async token => { var payment=await _payment.ProcessPaymentAsync(order.Id,order.TotalAmount,token); transactionId=payment.TransactionId; token.ThrowIfCancellationRequested(); if(!payment.Success) throw new PaymentDeclinedException(payment.Message ?? "Card declined by issuer"); },paymentCts.Token); }
                catch(OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    ct.ThrowIfCancellationRequested();
                    if(transactionId is not null) await _payment.RefundAsync(transactionId,CancellationToken.None);
                    if(stockReserved) await _inventory.ReleaseStockAsync(order,CancellationToken.None);
                    await SaveStatusAsync(OrderStatus.PaymentFailed,"Payment gateway timeout (3s)");
                    Log.Error($"Order #{order.Id}",$"Step 4/6 Payment ... TIMEOUT after 3000 ms, released"); return false;
                }
            }
            try
            {
                await Step(order,5,"Save completed order",async token => { order.TransactionId=transactionId; order.Status=OrderStatus.Completed; order.FailureReason=null; await _orders.SaveAsync(order,token); },ct);
            }
            catch(Exception ex) when (!ct.IsCancellationRequested && ex is DatabaseWriteException)
            {
                if(transactionId is not null) await _payment.RefundAsync(transactionId,CancellationToken.None);
                if(stockReserved) await _inventory.ReleaseStockAsync(order,CancellationToken.None);
                await SaveStatusAsync(OrderStatus.Failed,$"Database save failed after payment; payment {transactionId} refunded");
                Log.Error($"Order #{order.Id}",ex.Message); return false;
            }
            try { await Step(order,6,"Confirmation email",async token => await SendConfirmationWithRetryAsync(order,customer!,token),ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Log.Error($"Email Order #{order.Id}",$"Email could not be persisted; order remains Completed: {ex.Message}"); }
            Log.Success($"Order #{order.Id}",$"Completed in {total.ElapsedMilliseconds} ms"); return true;
        }
        catch(OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if(transactionId is not null) await _payment.RefundAsync(transactionId,CancellationToken.None);
            if(stockReserved) await _inventory.ReleaseStockAsync(order,CancellationToken.None);
            order.TransactionId=null; await SaveStatusAsync(OrderStatus.Pending,"Cancelled");
            Log.Error($"Order #{order.Id}",$"Cancelled after {total.ElapsedMilliseconds} ms; stock released"); throw;
        }
        catch(CustomerNotFoundException ex) when (!ct.IsCancellationRequested) { await SaveStatusAsync(OrderStatus.Rejected,ex.Message); return false; }
        catch(InvalidOperationException ex) when (!ct.IsCancellationRequested && (ex.Message=="Customer is inactive" || ex.Message.StartsWith("Insufficient stock",StringComparison.Ordinal)))
        { if(stockReserved) await _inventory.ReleaseStockAsync(order,CancellationToken.None); await SaveStatusAsync(OrderStatus.Rejected,ex.Message); return false; }
        catch(InvalidCouponException ex) when (!ct.IsCancellationRequested)
        { if(stockReserved) await _inventory.ReleaseStockAsync(order,CancellationToken.None); await SaveStatusAsync(OrderStatus.Rejected,ex.Message); return false; }
        catch(PaymentDeclinedException ex) when (!ct.IsCancellationRequested)
        { if(stockReserved) await _inventory.ReleaseStockAsync(order,CancellationToken.None); await SaveStatusAsync(OrderStatus.PaymentFailed,ex.Message); return false; }
        catch(Exception) when (ct.IsCancellationRequested)
        {
            if(transactionId is not null) await _payment.RefundAsync(transactionId,CancellationToken.None);
            if(stockReserved) await _inventory.ReleaseStockAsync(order,CancellationToken.None);
            order.TransactionId=null; await SaveStatusAsync(OrderStatus.Pending,"Cancelled");
            throw new OperationCanceledException(ct);
        }
        catch(Exception ex) when (!ct.IsCancellationRequested)
        { if(stockReserved) await _inventory.ReleaseStockAsync(order,CancellationToken.None); await SaveStatusAsync(OrderStatus.Failed,ex.Message); Log.Error($"Order #{order.Id}",ex.Message); return false; }
    }

    private async Task SendConfirmationWithRetryAsync(Order order, Customer customer, CancellationToken ct)
    {
        int[] waits={500,1000,2000}; string last="";
        for(int attempt=1;attempt<=4;attempt++)
        {
            try { await _email.SendConfirmationAsync(order,customer,ct); return; }
            catch(SmtpUnavailableException ex) when(attempt<4) { last=ex.Message; Log.Error($"Email Order #{order.Id}",$"Attempt {attempt} failed: {ex.Message}"); await Task.Delay(waits[attempt-1],ct); }
            catch(SmtpUnavailableException ex) { last=ex.Message; Log.Error($"Email Order #{order.Id}",$"Attempt {attempt} failed: {ex.Message}"); }
        }
        await new EmailRepository(_store).SaveRetryAsync(new EmailMessage { Id=Guid.NewGuid(),OrderId=order.Id,To=customer.Email,Subject=$"Order #{order.Id} confirmation",Body=$"Your order total is {order.TotalAmount:F2}.",SentAt=DateTime.Now,Attempts=4,LastError=last },ct);
        Log.Info("EmailService",$"Email for Order #{order.Id} queued for retry later");
    }

    private async Task<string> StockReasonAsync(Order order,CancellationToken ct)
    { foreach(var item in order.Items){var p=await _products.GetByIdAsync(item.ProductId,ct);if(p is null||p.StockQuantity<item.Quantity)return $"Insufficient stock for '{p?.Name ?? $"Product #{item.ProductId}"}'";}return "Insufficient stock"; }
    private static async Task Step(Order order,int step,string name,Func<CancellationToken,Task> action,CancellationToken ct)
    {
        var sw=Stopwatch.StartNew(); Log.Info($"Order #{order.Id}",$"Step {step}/6 {name} ... START");
        try { await action(ct); sw.Stop(); Log.Info($"Order #{order.Id}",$"Step {step}/6 {name} ... OK in {sw.ElapsedMilliseconds} ms"); }
        catch(OperationCanceledException) { sw.Stop(); Log.Error($"Order #{order.Id}",$"Step {step}/6 {name} ... CANCELLED after {sw.ElapsedMilliseconds} ms"); throw; }
        catch(Exception ex) { sw.Stop(); Log.Error($"Order #{order.Id}",$"Step {step}/6 {name} ... FAILED after {sw.ElapsedMilliseconds} ms: {ex.Message}"); throw; }
    }
    private sealed class PaymentDeclinedException(string message):Exception(message) { }
}
