using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Platform.Agent.Tests.Internals;

internal sealed class TestCapturingLogger(ConcurrentQueue<string> messages) : ILogger
{
  public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

  public bool IsEnabled(LogLevel logLevel) => true;

  public void Log<TState>(
    LogLevel logLevel,
    EventId eventId,
    TState state,
    Exception? exception,
    Func<TState, Exception?, string> formatter)
  {
    messages.Enqueue(formatter(state, exception));
  }
}
