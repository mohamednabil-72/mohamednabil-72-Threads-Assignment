using System.Collections.Concurrent;
using OrderProcessing;

namespace OrderProcessing.Phase4;

public sealed class CustomerDashboard
{
    public Customer? Customer { get; set; }
    public List<Order> Orders { get; set; } = new();
    public List<PaymentTransaction> Payments { get; set; } = new();
    public List<EmailMessage> Emails { get; set; } = new();
}

public sealed class CustomerDashboardService
{
    private readonly JsonFileStore _store; private readonly HashSet<int> _failPayments; private readonly ConcurrentDictionary<int,byte> _threads=new();
    public int DistinctThreadCount => _threads.Count;
    public CustomerDashboardService(JsonFileStore store,IEnumerable<int>? failPaymentsForCustomerIds=null)
    { _store=store;_failPayments=(failPaymentsForCustomerIds??Array.Empty<int>()).ToHashSet(); }
    public async Task<CustomerDashboard> LoadAsync(int customerId,CancellationToken ct)
    {
        Task<Customer?> profile=LoadProfile(customerId,ct);Task<List<Order>> orders=LoadOrders(customerId,ct);Task<List<PaymentTransaction>> payments=LoadPayments(customerId,ct);Task<List<EmailMessage>> emails=LoadEmails(customerId,ct);
        var all=new Task[]{profile,orders,payments,emails};var errors=new List<Exception>();
        try{await Task.WhenAll(all);}catch{foreach(var t in all)if(t.Exception is not null)errors.AddRange(t.Exception.Flatten().InnerExceptions);}
        if(errors.Count>0)throw new AggregateException(errors);
        return new CustomerDashboard{Customer=await profile,Orders=await orders,Payments=await payments,Emails=await emails};
    }
    private async Task<Customer?> LoadProfile(int id,CancellationToken ct){_threads.TryAdd(Environment.CurrentManagedThreadId,0);await Latency.QueryAsync(nameof(CustomerDashboardService),$"profile #{id}",ct);_threads.TryAdd(Environment.CurrentManagedThreadId,0);return (await _store.ReadAllAsync<Customer>(DataFiles.Customers,ct)).FirstOrDefault(x=>x.Id==id) ?? throw new CustomerNotFoundException($"Customer #{id} not found");}
    private async Task<List<Order>> LoadOrders(int id,CancellationToken ct){_threads.TryAdd(Environment.CurrentManagedThreadId,0);await Latency.QueryAsync(nameof(CustomerDashboardService),$"orders for customer #{id}",ct);_threads.TryAdd(Environment.CurrentManagedThreadId,0);return (await _store.ReadAllAsync<Order>(DataFiles.Orders,ct)).Where(x=>x.CustomerId==id).ToList();}
    private async Task<List<PaymentTransaction>> LoadPayments(int id,CancellationToken ct){_threads.TryAdd(Environment.CurrentManagedThreadId,0);await Latency.QueryAsync(nameof(CustomerDashboardService),$"payments for customer #{id}",ct);_threads.TryAdd(Environment.CurrentManagedThreadId,0);if(_failPayments.Contains(id))throw new TimeoutException("Payments store timeout");var orders=(await _store.ReadAllAsync<Order>(DataFiles.Orders,ct)).Where(x=>x.CustomerId==id).Select(x=>x.Id).ToHashSet();return (await _store.ReadAllAsync<PaymentTransaction>(DataFiles.Payments,ct)).Where(x=>orders.Contains(x.OrderId)).ToList();}
    private async Task<List<EmailMessage>> LoadEmails(int id,CancellationToken ct){_threads.TryAdd(Environment.CurrentManagedThreadId,0);await Latency.QueryAsync(nameof(CustomerDashboardService),$"emails for customer #{id}",ct);_threads.TryAdd(Environment.CurrentManagedThreadId,0);var orders=(await _store.ReadAllAsync<Order>(DataFiles.Orders,ct)).Where(x=>x.CustomerId==id).Select(x=>x.Id).ToHashSet();return (await _store.ReadAllAsync<EmailMessage>(DataFiles.Emails,ct)).Where(x=>orders.Contains(x.OrderId)).ToList();}
}
