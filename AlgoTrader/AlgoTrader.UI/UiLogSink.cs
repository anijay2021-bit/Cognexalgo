using System;
using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace AlgoTrader.UI;

/// <summary>Serilog Sink for UI diagnostics.</summary>
public class UiLogSink : ILogEventSink
{
    private static readonly ConcurrentQueue<string> _logs = new();
    public static event Action<string>? OnMessage;

    public void Emit(LogEvent logEvent)
    {
        var msg = $"[{logEvent.Timestamp:HH:mm:ss} {logEvent.Level.ToString().Substring(0, 3).ToUpper()}] {logEvent.RenderMessage()}";
        if (logEvent.Exception != null) msg += $"\n{logEvent.Exception}";
        
        _logs.Enqueue(msg);
        OnMessage?.Invoke(msg);
    }

    public static IEnumerable<string> GetHistory() => _logs.ToArray();
}
