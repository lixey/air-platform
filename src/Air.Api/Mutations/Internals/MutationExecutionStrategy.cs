using Microsoft.EntityFrameworkCore.Storage;

namespace Platform.Api.Mutations.Internals;

internal sealed class MutationExecutionStrategy : ExecutionStrategy
{
  private MutationExecutionStrategy(DbContext db) : base(db, 0, TimeSpan.Zero)
  {
  }

  public static Task<TResult> RunAsync<TResult>(
    DbContext db,
    Func<CancellationToken, Task<TResult>> operation,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(operation);
    cancellationToken.ThrowIfCancellationRequested();

    if (Current is not null || db.Database.CurrentTransaction is not null || !db.Database.IsRelational())
    {
      return operation(cancellationToken);
    }

    return new MutationExecutionStrategy(db).ExecuteAsync(
      operation,
      static (_, run, token) => run(token),
      null,
      cancellationToken);
  }

  protected override bool ShouldRetryOn(Exception exception) => false;
}
