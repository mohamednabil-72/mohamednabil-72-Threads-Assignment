namespace OrderProcessing.Shared.Repositories;

public sealed class EmailRepository
{
    private const string Source = nameof(EmailRepository);

    private readonly JsonFileStore _store;

    public EmailRepository(JsonFileStore store)
    {
        _store = store;
    }

    public Task SaveAsync(EmailMessage email, CancellationToken ct)
    {
        return AppendAsync(DataFiles.Emails, email, ct);
    }

    public Task SaveRetryAsync(EmailMessage email, CancellationToken ct)
    {
        return AppendAsync(DataFiles.EmailRetry, email, ct);
    }

    private async Task AppendAsync(string fileName, EmailMessage email, CancellationToken ct)
    {
        bool added = false;

        await Latency.WriteAsync(
            Source,
            $"email for order #{email.OrderId} into {fileName}",
            async token =>
            {
                await _store.AppendAsync(fileName, email, token);
                added = true;
            },
            () => added
                ? _store.UpdateAsync<EmailMessage>(
                    fileName,
                    emails => emails.RemoveAll(e => e.Id == email.Id),
                    CancellationToken.None)
                : Task.CompletedTask,
            ct);

        Log.Success(Source, $"Email for order #{email.OrderId} saved to {fileName}");
    }
}
