namespace LiveDLSS.Core.Logging;

public static class Log
{
    private static readonly Lock Gate = new();

    public static void Info(string message) => Write("INF", message, Console.Out);
    public static void Warn(string message) => Write("WRN", message, Console.Error);
    public static void Error(string message) => Write("ERR", message, Console.Error);

    private static void Write(string level, string message, TextWriter writer)
    {
        lock (Gate)
        {
            writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}");
        }
    }
}
