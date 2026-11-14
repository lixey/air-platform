using Microsoft.EntityFrameworkCore.Storage;

namespace Platform.Data.Internals;

internal sealed class NonRetryingExecutionStrategy : ExecutionStrategy
{
  private NonRetryingExecutionStrategy(DbContext db) : base(db, 0, TimeSpan.Zero)
  {
  }

  public static Task RunAsync(
    DbContext db,
    Func<CancellationToken, Task> operation,
    CancellationToken ct)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(operation);
    ct.ThrowIfCancellationRequested();

    if (Current is not null || db.Database.CurrentTransaction is not null || !db.Database.IsRelational())
    {
      return operation(ct);
    }

    return new NonRetryingExecutionStrategy(db).ExecuteAsync(
      operation,
      static async (_, run, token) =>
      {
        await run(token);
        return true;
      },
      verifySucceeded: null,
      ct);
  }

  protected override bool ShouldRetryOn(Exception exception) => false;
}
