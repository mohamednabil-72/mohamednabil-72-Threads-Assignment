
namespace InvoiceExport;

public sealed class InvoiceFileWriter
{
    readonly string _folder;

    public InvoiceFileWriter(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(_folder);
    }

    public string WriteInvoice(Order order, CustomerProfile customer)
    {
        string path = Path.Combine(_folder, $"invoice-{order.Id}.txt");

        string body =
            $"""
            SwiftBite Invoice
            -----------------
            Order:      {order.Id}
            Restaurant: {order.Restaurant}
            Customer:   {customer.Name} <{customer.Email}>
            Total:      {order.Total:C}
            """;

        Thread.Sleep(300);
        File.WriteAllText(path, body);
        return path;
    }
}
