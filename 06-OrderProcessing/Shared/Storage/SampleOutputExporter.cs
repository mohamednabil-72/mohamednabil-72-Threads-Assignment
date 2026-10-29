namespace OrderProcessing.Shared.Storage;

public static class SampleOutputExporter
{
    public static async Task SaveAsync(JsonFileStore source, string phaseName, CancellationToken ct)
    {
        string directory = Path.Combine(Directory.GetCurrentDirectory(), "sample-output", phaseName);
        Directory.CreateDirectory(directory);
        var destination = new JsonFileStore(directory);
        await destination.WriteAllAsync(DataFiles.Customers, await source.ReadAllAsync<Customer>(DataFiles.Customers, ct), ct);
        await destination.WriteAllAsync(DataFiles.Products, await source.ReadAllAsync<Product>(DataFiles.Products, ct), ct);
        await destination.WriteAllAsync(DataFiles.Coupons, await source.ReadAllAsync<Coupon>(DataFiles.Coupons, ct), ct);
        await destination.WriteAllAsync(DataFiles.IncomingOrders, await source.ReadAllAsync<Order>(DataFiles.IncomingOrders, ct), ct);
        await destination.WriteAllAsync(DataFiles.Orders, await source.ReadAllAsync<Order>(DataFiles.Orders, ct), ct);
        await destination.WriteAllAsync(DataFiles.Payments, await source.ReadAllAsync<PaymentTransaction>(DataFiles.Payments, ct), ct);
        await destination.WriteAllAsync(DataFiles.Emails, await source.ReadAllAsync<EmailMessage>(DataFiles.Emails, ct), ct);
        await destination.WriteAllAsync(DataFiles.EmailRetry, await source.ReadAllAsync<EmailMessage>(DataFiles.EmailRetry, ct), ct);
        string reportsDirectory = Path.Combine(DataFiles.DirectoryPath, DataFiles.ReportsFolder);
        if (Directory.Exists(reportsDirectory))
        {
            string snapshotReports = Path.Combine(directory, DataFiles.ReportsFolder);
            Directory.CreateDirectory(snapshotReports);
            foreach (string report in Directory.GetFiles(reportsDirectory, "report_*.json"))
                File.Copy(report, Path.Combine(snapshotReports, Path.GetFileName(report)), overwrite: true);
        }
        Log.Success(nameof(SampleOutputExporter), $"Saved {phaseName} JSON snapshot to {directory}");
    }
}
