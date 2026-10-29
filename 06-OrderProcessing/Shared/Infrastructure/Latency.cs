namespace OrderProcessing.Shared.Infrastructure;

public static class Latency
{
    public static int Between(int min, int max)
    {
        return Random.Shared.Next(min, max + 1);
    }

    public static async Task StepAsync(string source, string message, int delayMs, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Log.Info(source, message);
        await Task.Delay(delayMs, ct);
    }

    public static async Task QueryAsync(string source, string description, CancellationToken ct)
    {
        await StepAsync(source, "Opening connection...", 200, ct);
        await StepAsync(source, $"Executing query for {description}...", Between(300, 600), ct);
    }

    public static async Task WriteAsync(
        string source,
        string description,
        Func<CancellationToken, Task> apply,
        Func<Task>? rollback,
        CancellationToken ct)
    {
        bool applyStarted = false;
        try
        {
            await StepAsync(source, "Opening connection...", 200, ct);
            await StepAsync(source, "Beginning transaction...", 100, ct);
            await StepAsync(source, $"Writing {description}...", 400, ct);
            applyStarted = true;
            await apply(ct);
            await StepAsync(source, "Committing transaction...", 200, ct);
        }
        catch
        {
            if (applyStarted && rollback is not null)
            {
                await rollback();
                Log.Error(source, $"Transaction for {description} rolled back");
            }
            throw;
        }
    }

    public static async Task ExternalApiAsync(string source, string description, CancellationToken ct)
    {
        await StepAsync(source, "DNS lookup and handshake...", 150, ct);
        await StepAsync(source, $"Sending request for {description}...", 300, ct);
        await StepAsync(source, "Waiting for response...", Between(500, 1500), ct);
    }

    public static async Task PaymentGatewayAsync(string source, string description, CancellationToken ct)
    {
        await StepAsync(source, $"Authorizing payment for {description}...", 800, ct);
        await StepAsync(source, "Running fraud check...", 500, ct);
        await StepAsync(source, "Capturing payment...", 700, ct);
    }

    public static async Task SmtpAsync(string source, string description, CancellationToken ct)
    {
        await StepAsync(source, $"Rendering email template for {description}...", 200, ct);
        await StepAsync(source, "Connecting to SMTP server...", 300, ct);
        await StepAsync(source, "Sending email...", 500, ct);
    }
}
