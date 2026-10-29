namespace OrderProcessing.Phase2;

public static class BlockingWaitDemo
{
    // Deliberately blocking demonstration only. Production flow uses await.
    public static void Run()
    {
        var resultTask = Task.Run(async () => { await Task.Delay(100, CancellationToken.None); return 42; });
        int result = resultTask.Result; // Deliberately demonstrates blocking a thread.
        Log.Plain($"Blocking .Result demo returned {result}");
        var waitTask = Task.Run(() => Thread.Sleep(100));
        waitTask.Wait(); // Deliberately demonstrates blocking a thread.
        Log.Plain("Blocking .Wait() demo finished");
    }
}
