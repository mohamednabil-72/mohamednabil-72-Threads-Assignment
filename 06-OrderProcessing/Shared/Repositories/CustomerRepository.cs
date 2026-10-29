namespace OrderProcessing.Shared.Repositories;

public sealed class CustomerRepository
{
    private const string Source = nameof(CustomerRepository);

    private readonly JsonFileStore _store;

    public CustomerRepository(JsonFileStore store)
    {
        _store = store;
    }

    public async Task<Customer?> GetByIdAsync(int id, CancellationToken ct)
    {
        await Latency.QueryAsync(Source, $"Customer #{id}", ct);

        List<Customer> customers = await _store.ReadAllAsync<Customer>(DataFiles.Customers, ct);
        Customer? customer = customers.Find(c => c.Id == id);

        Log.Info(Source, customer is null
            ? $"Customer #{id} not found"
            : $"Customer #{id} found ({customer.Name})");

        return customer;
    }
}
