using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Platform.App.Tests.Internals;

internal sealed class SingleActiveReaderInterceptor : DbCommandInterceptor
{
  private int _activeReaders;

  public int ActiveReaders => Volatile.Read(ref _activeReaders);

  public override InterceptionResult<DbDataReader> ReaderExecuting(
    DbCommand command,
    CommandEventData eventData,
    InterceptionResult<DbDataReader> result)
  {
    EnsureNoActiveReader();
    return result;
  }

  public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
    DbCommand command,
    CommandEventData eventData,
    InterceptionResult<DbDataReader> result,
    CancellationToken cancellationToken = default)
  {
    EnsureNoActiveReader();
    return ValueTask.FromResult(result);
  }

  public override DbDataReader ReaderExecuted(
    DbCommand command,
    CommandExecutedEventData eventData,
    DbDataReader result)
  {
    Interlocked.Increment(ref _activeReaders);
    return result;
  }

  public override ValueTask<DbDataReader> ReaderExecutedAsync(
    DbCommand command,
    CommandExecutedEventData eventData,
    DbDataReader result,
    CancellationToken cancellationToken = default)
  {
    Interlocked.Increment(ref _activeReaders);
    return ValueTask.FromResult(result);
  }

  public override InterceptionResult DataReaderClosing(
    DbCommand command,
    DataReaderClosingEventData eventData,
    InterceptionResult result)
  {
    Interlocked.Decrement(ref _activeReaders);
    return result;
  }

  public override ValueTask<InterceptionResult> DataReaderClosingAsync(
    DbCommand command,
    DataReaderClosingEventData eventData,
    InterceptionResult result)
  {
    Interlocked.Decrement(ref _activeReaders);
    return ValueTask.FromResult(result);
  }

  private void EnsureNoActiveReader()
  {
    if (Volatile.Read(ref _activeReaders) > 0)
    {
      throw new InvalidOperationException("A command is already in progress on this connection (non-MARS).");
    }
  }
}
