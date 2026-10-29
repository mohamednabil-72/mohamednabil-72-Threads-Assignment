namespace OrderProcessing.Shared.Logging;

public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string source, string message)
    {
        Write(null, source, message);
    }

    public static void Success(string source, string message)
    {
        Write(ConsoleColor.Green, source, message);
    }

    public static void Error(string source, string message)
    {
        Write(ConsoleColor.Red, source, message);
    }

    public static void Plain(string message)
    {
        lock (Gate)
        {
            Console.WriteLine(message);
        }
    }

    private static void Write(ConsoleColor? color, string source, string message)
    {
        lock (Gate)
        {
            string line =
                $"[{DateTime.Now:HH:mm:ss.fff}] [Thread {Environment.CurrentManagedThreadId}] {source} -> {message}";

            if (color is { } value)
            {
                Console.ForegroundColor = value;
                Console.WriteLine(line);
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine(line);
            }
        }
    }
}
