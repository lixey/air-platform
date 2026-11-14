using Microsoft.EntityFrameworkCore.Storage;

namespace Platform.Data.Tests.Internals;

internal sealed class TestRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
  : ExecutionStrategy(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.Zero)
{
  protected override bool ShouldRetryOn(Exception exception) => exception is TimeoutException;
}
