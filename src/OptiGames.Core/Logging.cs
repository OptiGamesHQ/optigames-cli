namespace OptiGames.Core;

/// <summary>
/// Sink for the human-readable action log surfaced in the UI.
/// Implementations must be thread-safe or marshal to the UI thread themselves,
/// because Apply/Revert run on background threads.
/// </summary>
public interface ILogSink
{
    void Write(string message);
}

/// <summary>Routes log messages to a delegate (the UI supplies one).</summary>
public sealed class DelegateLogSink : ILogSink
{
    private readonly Action<string> _write;
    public DelegateLogSink(Action<string> write) => _write = write;
    public void Write(string message) => _write(message);
}

/// <summary>Discards all log output.</summary>
public sealed class NullLogSink : ILogSink
{
    public static readonly NullLogSink Instance = new();
    public void Write(string message) { }
}
