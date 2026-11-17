using System.Collections.Concurrent;

namespace MaxiDodPS3;

/// <summary>
/// One place for all runtime messages. Keeps a ring buffer (for the debugger window), appends to a log file
/// in %LOCALAPPDATA%\MaxiDodPS3\log.txt and echoes to the console when one is attached.
/// </summary>
public static class Log
{
    public enum Level { Info, Warn, Error }

    public sealed record Entry(DateTime Time, Level Level, string Text);

    private static readonly ConcurrentQueue<Entry> Buffer = new();
    private static readonly object FileLock = new();
    private const int MaxBuffered = 2000;

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaxiDodPS3");

    public static string FilePath => Path.Combine(Folder, "log.txt");

    /// <summary>Raised on whatever thread logged. UI code must marshal to its own thread.</summary>
    public static event Action<Entry>? Written;

    public static void Info(string text) => Add(Level.Info, text);
    public static void Warn(string text) => Add(Level.Warn, text);
    public static void Error(string text) => Add(Level.Error, text);

    private static void Add(Level level, string text)
    {
        var e = new Entry(DateTime.Now, level, text);
        Buffer.Enqueue(e);
        while (Buffer.Count > MaxBuffered && Buffer.TryDequeue(out _)) { }

        try { Console.WriteLine(text); } catch { }

        try
        {
            lock (FileLock)
            {
                Directory.CreateDirectory(Folder);
                // Keep the file from growing forever: start over past 1 MB.
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 1_000_000) File.Move(FilePath, FilePath + ".old", true);
                File.AppendAllText(FilePath, $"{e.Time:yyyy-MM-dd HH:mm:ss.fff} [{level}] {text}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the app */ }

        try { Written?.Invoke(e); } catch { }
    }

    public static Entry[] Snapshot() => Buffer.ToArray();
}
