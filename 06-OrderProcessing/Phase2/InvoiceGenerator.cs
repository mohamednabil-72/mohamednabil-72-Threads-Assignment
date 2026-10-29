using System.Text;
using OrderProcessing;

namespace OrderProcessing.Phase2;

public sealed class InvoiceGenerator(JsonFileStore store)
{
    public async Task GenerateAsync(Order order, CancellationToken ct)
    {
        await Task.Run(async () =>
        {
            long value = order.Id;
            for (int i = 0; i < 20_000_000; i++) { if ((i & 0xFFFF) == 0) ct.ThrowIfCancellationRequested(); value = (value * 31 + i) % 1_000_000_007; }
            ct.ThrowIfCancellationRequested();
            string directory = Path.Combine(DataFiles.DirectoryPath, DataFiles.InvoicesFolder);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"invoice_{order.Id}.txt");
            string content = $"Invoice for Order #{order.Id}\nSubtotal: {order.SubTotal:F2}\nDiscount: {order.Discount:F2}\nShipping: {order.ShippingCost:F2}\nTotal: {order.TotalAmount:F2}\nRender checksum: {value}\n";
            string temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, content, new UTF8Encoding(false), ct);
            File.Move(temp, path, true);
        }, ct);
        Log.Success(nameof(InvoiceGenerator), $"Invoice for order #{order.Id} generated");
    }
}
