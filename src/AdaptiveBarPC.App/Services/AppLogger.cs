using System.IO;

namespace AdaptiveBarPC.Services;

public sealed class AppLogger
{
    private readonly string _file;
    private readonly object _gate = new();

    public AppLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        _file = Path.Combine(logDirectory, "adaptivebar.log");
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", message, ex);

    private void Write(string level, string message, Exception? ex)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:O} [{level}] {message}";
            if (ex is not null)
                line += Environment.NewLine + ex;

            lock (_gate)
            {
                File.AppendAllText(_file, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
