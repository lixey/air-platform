using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Platform.Agent.Tests.Internals;

internal sealed class TestCapturingLoggerProvider : ILoggerProvider
{
  public ConcurrentQueue<string> Messages { get; } = new();

  public ILogger CreateLogger(string categoryName) => new TestCapturingLogger(Messages);

  public void Dispose()
  {
  }
}
