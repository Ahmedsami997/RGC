namespace RGC.MacAgent.Services;

/// <summary>Tiny daily-rolling file log in ~/Library/Logs/RGC (visible in Console.app).</summary>
public static class AgentLog
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            var file = Path.Combine(MacPaths.LogDir, $"agent-{DateTime.Now:yyyyMMdd}.log");
            lock (Gate)
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {level} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the agent down.
        }
    }
}
