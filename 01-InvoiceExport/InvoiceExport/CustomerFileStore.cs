namespace InvoiceExport;

public sealed class CustomerFileStore
{
    readonly string _folder;

    public CustomerFileStore(string folder) => _folder = folder;

    public CustomerProfile ReadProfile(int customerId)
    {
        string path = Path.Combine(_folder, $"customer-{customerId}.txt");
        Thread.Sleep(300);
        string text = File.ReadAllText(path);
        string[] parts = text.Split('|');
        return new CustomerProfile(int.Parse(parts[0]), parts[1], parts[2]);
    }
}
