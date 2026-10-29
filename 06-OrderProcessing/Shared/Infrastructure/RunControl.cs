namespace OrderProcessing.Shared.Infrastructure;

public sealed class RunControl : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ConsoleCancelEventHandler _handler;
    private volatile bool _interrupted;

    public RunControl(int? cancelAfterMs)
    {
        _handler = OnCancelKeyPress;
        Console.CancelKeyPress += _handler;

        if (cancelAfterMs is { } ms)
        {
            Log.Info("Run", $"The run will be cancelled automatically after {ms} ms");
            _cts.CancelAfter(ms);
        }
    }

    public CancellationToken Token => _cts.Token;

    public bool IsCancelled => _cts.IsCancellationRequested;

    public static RunControl FromArgs(string[] args)
    {
        int? cancelAfter = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--cancel-after", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out int ms) || ms < 0)
            {
                throw new ArgumentException(
                    "--cancel-after needs a non-negative number of milliseconds, for example --cancel-after 4000");
            }

            cancelAfter = ms;
        }

        return new RunControl(cancelAfter);
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= _handler;
        _cts.Dispose();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        if (_interrupted)
        {
            return;
        }

        _interrupted = true;
        e.Cancel = true;

        if (!_cts.IsCancellationRequested)
        {
            Log.Error("Run", "Ctrl+C received -> cancelling the run (press Ctrl+C again to force quit)");
            _cts.Cancel();
        }
        else
        {
            Log.Error("Run", "Ctrl+C received -> the run is already cancelling (press Ctrl+C again to force quit)");
        }
    }
}

public sealed class RunSummary
{
    private static int _finished;
    private static int _cancelled;

    public int Finished => Volatile.Read(ref _finished);

    public int Cancelled => Volatile.Read(ref _cancelled);

    public void OrderFinished() => Interlocked.Increment(ref _finished);

    public void OrderCancelled() => Interlocked.Increment(ref _cancelled);

    public static void Reset() { Interlocked.Exchange(ref _finished, 0); Interlocked.Exchange(ref _cancelled, 0); }

    public string CancelledMessage()
    {
        return $"Run cancelled — {Finished} orders finished, {Cancelled} cancelled";
    }

    public void PrintCancelled()
    {
        Log.Plain(CancelledMessage());
    }
}
