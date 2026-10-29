using System.Text;
using OrderProcessing.Phase1;
using OrderProcessing.Phase2;
using OrderProcessing.Phase3;
using OrderProcessing.Phase4;

namespace OrderProcessing;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string mode;
        try { mode = await ResolveModeAsync(args); }
        catch (ArgumentException ex) { Log.Error("Program", ex.Message); Environment.ExitCode = 2; return; }
        if (mode == "exit") return;
        RunControl run;
        try { run = RunControl.FromArgs(args); }
        catch (ArgumentException ex) { Log.Error("Program", ex.Message); Environment.ExitCode = 2; return; }
        using (run)
        {
            var store = new JsonFileStore();
            try
            {
                switch (mode)
                {
                    case "seed": await DataSeeder.ResetAsync(store, run.Token); break;
                    case "selftest": Environment.ExitCode = await SelfTest.RunAsync(store, run.Token) ? 0 : 1; return;
                    case "phase1": await new Phase1Runner(store).RunAsync(run.Token); break;
                    case "phase2": await new Phase2Runner(store).RunAsync(run.Token); break;
                    case "phase3": await new Phase3Runner(store).RunAsync(run.Token); break;
                    case "phase4": await new Phase4Runner(store).RunAsync(run.Token); break;
                    case "all":
                        await new Phase1Runner(store).RunAsync(run.Token);
                        await new Phase2Runner(store).RunAsync(run.Token);
                        await new Phase3Runner(store).RunAsync(run.Token);
                        await new Phase4Runner(store).RunAsync(run.Token);
                        break;
                    default: PrintUsage(); Environment.ExitCode = 2; return;
                }
                Environment.ExitCode = 0; return;
            }
            catch (OperationCanceledException)
            {
                new RunSummary().PrintCancelled();
                await VerifyFilesAsync(store);
                Environment.ExitCode = 130; return;
            }
            catch (DataFileCorruptedException ex) { Log.Error("Program", ex.Message); Environment.ExitCode = 3; return; }
            catch (Exception ex) { Log.Error("Program", $"Unhandled {ex.GetType().Name}: {ex.Message}"); Environment.ExitCode = 1; return; }
        }
    }

    private static async Task<string> ResolveModeAsync(string[] args)
    {
        string? mode = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        if (mode is not null) return mode.ToLowerInvariant();
        Log.Plain("E-Commerce Order Processing System");
        Log.Plain("1. Phase 1 — Async Data Access and Validation");
        Log.Plain("2. Phase 2 — Concurrent Processing");
        Log.Plain("3. Phase 3 — Full Checkout Workflow");
        Log.Plain("4. Phase 4 — Scale-Up");
        Log.Plain("5. Run all phases");
        Log.Plain("6. Seed/reset data only");
        Log.Plain("7. Run self-tests");
        Log.Plain("0. Exit");
        Log.Plain("Choose an option: ");
        string? input = Console.ReadLine();
        return input?.Trim() switch { "1" => "phase1", "2" => "phase2", "3" => "phase3", "4" => "phase4", "5" => "all", "6" => "seed", "7" => "selftest", "0" or null => "exit", _ => "help" };
    }

    private static void PrintUsage()
    {
        Log.Plain("Usage: dotnet run -- <phase1|phase2|phase3|phase4|all|seed|selftest> [--cancel-after <ms>]");
        Log.Plain("No argument opens a numbered menu.");
    }

    private static async Task VerifyFilesAsync(JsonFileStore store)
    {
        bool allValid = true;
        foreach (string file in DataFiles.All)
        {
            try { await store.CountRecordsAsync(file, CancellationToken.None); }
            catch (DataFileCorruptedException ex) { allValid = false; Log.Error("Program", ex.Message); }
        }
        if (allValid) Log.Success("Program", "Every data file is still valid JSON after the cancelled run");
    }
}
