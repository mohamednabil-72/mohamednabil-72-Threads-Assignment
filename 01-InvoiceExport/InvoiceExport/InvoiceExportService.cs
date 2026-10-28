
using System.Diagnostics;

namespace InvoiceExport;

public sealed class InvoiceExportService
{
    readonly OrderDatabase _db;
    readonly CustomerFileStore _customers;
    readonly InvoiceFileWriter _invoices;

    public InvoiceExportService(
        OrderDatabase db,
        CustomerFileStore customers,
        InvoiceFileWriter invoices)
    {
        _db = db;
        _customers = customers;
        _invoices = invoices;
    }

    public void ExportAllPending()
    {
        var watch = Stopwatch.StartNew();

        List<Order> orders = _db.GetPendingOrders();

        Dictionary<int, CustomerProfile> customerCache = new();

        foreach (Order order in orders)
        {
            if (!customerCache.TryGetValue(order.CustomerId, out CustomerProfile? customer))
            {
                customer = _customers.ReadProfile(order.CustomerId);
                customerCache.Add(order.CustomerId, customer);
            }

            string path = _invoices.WriteInvoice(order, customer);

            Console.WriteLine($"Wrote {path}");
        }

        Console.WriteLine($"Export finished in {watch.Elapsed.TotalSeconds:F1} s");
    }
}
