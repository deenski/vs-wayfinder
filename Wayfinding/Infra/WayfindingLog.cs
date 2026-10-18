using System;
using System.Collections.Generic;

namespace Wayfinding.Infra;

public enum LogLevel
{
    Error,
    Warning,
    Notice,
    Debug,
    Detail,
    Trace,
}

public interface ILogSink
{
    void Write(LogLevel level, string line);
}

public interface IWayfindingLog
{
    int Verbosity { get; set; }

    void Log(LogLevel level, string message);

    void LogOnce(string key, LogLevel level, string message);
}

public sealed class WayfindingLog : IWayfindingLog
{
    public const string Prefix = "[wayfinding]";

    private readonly ILogSink sink;
    private readonly HashSet<string> once = new(StringComparer.Ordinal);
    private readonly object onceLock = new();
    private volatile int verbosity;

    public WayfindingLog(ILogSink sink, int verbosity = 0)
    {
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        this.verbosity = ClampVerbosity(verbosity);
    }

    public int Verbosity
    {
        get => verbosity;
        set => verbosity = ClampVerbosity(value);
    }

    public void Log(LogLevel level, string message)
    {
        if (verbosity < RequiredVerbosity(level))
        {
            return;
        }

        sink.Write(level, PrefixLine(message));
    }

    public void LogOnce(string key, LogLevel level, string message)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A log-once key is required.", nameof(key));
        }

        if (verbosity < RequiredVerbosity(level))
        {
            return;
        }

        lock (onceLock)
        {
            if (!once.Add(key))
            {
                return;
            }
        }

        sink.Write(level, PrefixLine(message));
    }

    public static int ClampVerbosity(int value)
    {
        if (value < 0)
        {
            return 0;
        }

        if (value > 3)
        {
            return 3;
        }

        return value;
    }

    public static int RequiredVerbosity(LogLevel level)
    {
        return level switch
        {
            LogLevel.Debug => 1,
            LogLevel.Detail => 2,
            LogLevel.Trace => 3,
            _ => 0,
        };
    }

    public static string PrefixLine(string message)
    {
        if (message.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return message;
        }

        return Prefix + " " + message;
    }

    public static string WithoutPrefix(string line)
    {
        var tagged = Prefix + " ";
        if (line.StartsWith(tagged, StringComparison.Ordinal))
        {
            return line.Substring(tagged.Length);
        }

        if (line.Equals(Prefix, StringComparison.Ordinal))
        {
            return "";
        }

        return line;
    }
}
